using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Contracts;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Modules.Agents;

internal sealed class AgentsOptions
{
    public const string Section = "Agents";
    /// <summary>Dossier que l'agent a le droit de lire. Choisi côté serveur, jamais par le client.</summary>
    public string? Workspace { get; set; }

    public int MaxIterations { get; set; } = 10;

    // Le bac à sable des commandes (ADR 0015).
    public string SandboxImage { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";
    public int SandboxTimeoutSeconds { get; set; } = 120;

    /// <summary>« none » (par défaut) : rien ne sort du conteneur ; « bridge » : réseau ouvert, décidé par l'administrateur.</summary>
    public string SandboxNetwork { get; set; } = "none";
}

/// <summary>
/// Exécute une mission avec Agent Framework.
/// Question : l'Analyste seul. Revue : l'Analyste rend ses constats, puis le Rédacteur écrit REPORT.md (soumis à approbation).
/// </summary>
// Dépendances inter-modules en paramètres du constructeur : le test d'architecture les voit.
internal sealed class AgentFrameworkRunner(
    IOptions<AgentsOptions> options,
    AuditLog audit,
    ILogger<AgentFrameworkRunner> logger,
    ILlmClientProvider llm,
    ILlmPricing pricing,
    IInstanceSettings instance,
    AgentDefinitions definitions,
    AgentCatalog catalog,
    AgentsDb db,
    ICommandSandbox sandbox) : IAgentRunner
{
    // Les types de Microsoft.Extensions.AI (messages, appels d'outils, demandes d'approbation) savent se sérialiser.
    private static readonly JsonSerializerOptions Json = AIJsonUtilities.DefaultOptions;

    // Les compétences approuvées pendant cette exécution (propose_skill) : MissionRunner les range sur le collègue.
    private readonly List<AgentLearned> _learned = [];

    public async Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace,
        IReadOnlyDictionary<string, AgentSeat>? seats, string? agent, string? launchedBy, CancellationToken cancellationToken,
        Func<string, Task>? onText = null)
    {
        var settings = options.Value;
        // Les réglages de l'instance (admin › Réglages) l'emportent sur la configuration de départ (ADR 0010).
        var tuning = await instance.GetAsync(cancellationToken);
        // Limite de tours : celle du rôle (ADR 0011), sinon celle de l'instance, sinon la configuration.
        var instanceMax = tuning.MaxIterations ?? settings.MaxIterations;
        // Le dossier du projet (choisi par un administrateur de l'instance) ; null : celui de la configuration,
        // que MissionRunner n'autorise que pour les projets d'un administrateur et les missions antérieures aux projets.
        workspace ??= settings.Workspace;
        if (string.IsNullOrWhiteSpace(workspace))
            throw new InvalidOperationException(
                "Workspace absent. Lance : dotnet user-secrets set \"Agents:Workspace\" \"<dossier>\" --project src/AgentYattus.Api");
        IReadOnlyList<AIFunction> tools =
        [
            .. new WorkspaceTools(workspace, tuning.MaxFileKb * 1024L, tuning.ExcludedFiles, sandbox).AsAIFunctions(),
            .. new GitTools(workspace, tuning.MaxFileKb * 1024).AsAIFunctions(),
        ];

        var usage = new AgentUsage(0, 0, 0);
        Stage stage;
        if (resume is not null)
        {
            stage = Stage.Parse(resume.Conversation);
            // Une réponse de l'humain à une mission terminée, ou sa décision sur la demande en attente.
            stage.History.Add(resume.FollowUp is { } followUp
                ? new ChatMessage(ChatRole.User, followUp)
                : new ChatMessage(ChatRole.User, AnswerPendingRequests(stage.History, resume.Approved)));
        }
        else if (kind == MissionKind.Question)
        {
            // L'agent choisi au lancement (ADR 0013), sinon l'Analyste.
            stage = new Stage(agent ?? AgentRoles.Analyst.Key, [new ChatMessage(ChatRole.User, goal)], Findings: null);
        }
        else
        {
            // Revue, étape 1 : l'Analyste explore et rend ses constats. Il n'a aucun outil d'écriture : il ne s'arrête jamais.
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Analyst.Key}", cancellationToken);
            var (analysis, analysisUsage) = await RunRoleAsync(missionId, AgentRoles.Analyst, tools, instanceMax,
                [new ChatMessage(ChatRole.User, AgentRoles.ReviewBrief(goal))], seats, launchedBy, cancellationToken);
            usage += analysisUsage;
            var findings = analysis.Text;

            // Étape 2, le passage de relais : le Rédacteur part des constats, pas de zéro.
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Writer.Key}", cancellationToken);
            stage = new Stage(AgentRoles.Writer.Key, [new ChatMessage(ChatRole.User, AgentRoles.Handoff(goal, findings))], findings);
        }

        // Même retiré entre-temps, l'agent d'une mission en pause la termine : la décision humaine reste possible.
        var role = await catalog.FindAsync(stage.Role, cancellationToken)
                   ?? throw new InvalidOperationException($"L'agent « {stage.Role} » n'existe plus.");
        var (response, roleUsage) = await RunRoleAsync(missionId, role, tools, instanceMax, stage.History, seats, launchedBy, cancellationToken, onText);
        usage += roleUsage;

        var requests = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
        if (requests.Count > 0)
        {
            // La boucle s'est arrêtée avant l'action sensible. On garde de quoi reprendre exactement ici.
            stage.History.AddRange(response.Messages);
            var calls = requests.Select(r => r.ToolCall).OfType<FunctionCallContent>()
                .Select(c => new AgentToolCall(c.Name, JsonSerializer.Serialize(c.Arguments, Json)))
                .ToList();
            foreach (var call in calls)
                await audit.AppendAsync(missionId, $"approbation-demandee:{call.Tool}", cancellationToken);
            return new AgentOutcome(null, new AgentApproval(calls), stage.Serialize(), usage, _learned);
        }

        // Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
        logger.LogInformation(
            "Mission {MissionId} ({Role}) : {Tokens} tokens, réponse de {Length} caractères",
            missionId, role.Key, response.Usage?.TotalTokenCount, response.Text.Length);
        // La conversation complète est gardée : on pourra répondre à l'agent (leçon 117). Les constats ne sont
        // ajoutés qu'à la première réponse d'une revue.
        stage.History.AddRange(response.Messages);
        var answer = stage.Findings is null || resume?.FollowUp is not null
            ? response.Text
            : $"{response.Text}\n\n---\nConstats de l'Analyste :\n{stage.Findings}";
        return new AgentOutcome(answer, Conversation: stage.Serialize(), Usage: usage, Learned: _learned);
    }

    private async Task<(AgentResponse Response, AgentUsage Usage)> RunRoleAsync(
        Guid missionId, AgentRole role, IReadOnlyList<AIFunction> allTools, int instanceMax,
        List<ChatMessage> history, IReadOnlyDictionary<string, AgentSeat>? seats, string? launchedBy, CancellationToken cancellationToken,
        Func<string, Task>? onText = null)
    {
        // La consigne et la limite en vigueur pour ce rôle ; les garde-fous sont toujours ajoutés par le code.
        var definition = await definitions.CurrentAsync(role.Key, cancellationToken);
        var maxIterations = definition?.MaxIterations ?? instanceMax;
        var seat = seats?.GetValueOrDefault(role.Key);
        // Les compétences du collègue (leçon 104) ; une compétence retirée depuis n'est plus chargée.
        // Dans un projet partagé, celle d'Alice ne sert qu'aux missions d'Alice : Bob ne la lit pas en la faisant réciter à l'agent.
        var keys = seat?.Skills ?? [];
        var skills = keys.Count == 0 ? [] : await db.Skills.AsNoTracking()
            .Where(s => keys.Contains(s.Key) && s.Active && (s.OwnerId == null || s.OwnerId == launchedBy))
            .OrderBy(s => s.Key).ToListAsync(cancellationToken);
        // Apprendre suppose un collègue sur qui ranger la compétence, et quelqu'un à qui elle appartiendra.
        var learn = seat is not null && launchedBy is not null;
        var instructions = AgentRoles.Compose(role, definition?.Instructions, seat?.Style, [.. skills.Select(s => (s.Key, s.Description))], learn);

        // Chaque rôle a son modèle : le sien s'il en a un, sinon celui par défaut.
        var client = await llm.GetAsync(role.Key, cancellationToken) ?? throw new InvalidOperationException(
            "Aucun modèle : connecte un fournisseur dans Paramètres › Modèles, ou lance " +
            "dotnet user-secrets set \"Anthropic:ApiKey\" \"<ta-clé>\" --project src/AgentYattus.Api");
        await audit.AppendAsync(missionId, $"modele:{client.Model}", cancellationToken);

        // Chaque rôle ne reçoit que ses outils : l'Analyste ne peut pas écrire, même s'il le demandait.
        var tools = allTools.Where(t => role.Tools.Contains(t.Name)).ToList();
        if (skills.Count > 0)
            tools.Add(UseSkillFunction(missionId, role.Key, [.. skills.Select(s => s.Key)], cancellationToken));
        if (learn)
            tools.Add(ProposeSkillFunction(missionId, role.Key, launchedBy!, cancellationToken));
        var agent = CreateAgent(role, instructions, client.Client, tools, maxIterations);
        // En flux (leçon 119) : chaque morceau de texte part tout de suite vers qui regarde ; la réponse complète
        // (appels d'outils, demandes d'accord, tokens) est reconstituée ensuite à partir des mêmes morceaux.
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync(history, cancellationToken: cancellationToken))
        {
            updates.Add(update);
            if (onText is not null && update.Text is { Length: > 0 } text)
                await onText(text);
        }
        var response = updates.ToAgentResponse();

        // Mode Gratuit : chaque bascule vers le modèle suivant de la chaîne entre dans le journal.
        if (client.Client.GetService<FallbackChatClient>() is { } fallback)
            foreach (var used in fallback.Used.Skip(1))
                await audit.AppendAsync(missionId, $"bascule:{used}", cancellationToken);

        var toolTurns = 0;
        foreach (var message in response.Messages)
        {
            var calls = message.Contents.OfType<FunctionCallContent>().ToList();
            if (calls.Count > 0)
                toolTurns++;
            foreach (var call in calls)
            {
                await audit.AppendAsync(missionId, $"outil:{ToolName(call.Name)}", cancellationToken);
                // Qui a fait quoi, sur quel fichier : de quoi faire parler le bon collègue dans le bureau (ADR 0012).
                await audit.AppendAsync(missionId, ActionEvent(role.Key, call), cancellationToken);
            }
        }

        // Le framework ne signale pas la limite : il rappelle le modèle sans outils pour forcer une réponse.
        // Autant de tours d'outils que la limite veut dire que la réponse a été forcée (voir la leçon 08).
        if (toolTurns >= maxIterations)
            await audit.AppendAsync(missionId, "limite-de-tours", cancellationToken);

        // Les tokens de tous les tours d'outils, cumulés par la boucle ; le coût au prix du catalogue (ADR 0010).
        // En mode Gratuit, le dernier modèle de la chaîne a fait le travail : il est gratuit, comme les autres.
        var input = response.Usage?.InputTokenCount ?? 0;
        var output = response.Usage?.OutputTokenCount ?? 0;
        await audit.AppendAsync(missionId, $"usage:{role.Key}:{input}:{output}", cancellationToken);
        return (response, new AgentUsage(input, output, pricing.CostMicros(client.Model, input, output)));
    }

    /// <summary>
    /// Le deuxième étage du chargement progressif : la procédure, lue en base au moment où l'agent la demande.
    /// Seulement les compétences de ce collègue : le nom vient du modèle, il ne peut pas en ouvrir une autre.
    /// Chaque chargement est compté (Uses) et entre dans le journal : on sait quelles compétences servent.
    /// </summary>
    private AIFunction UseSkillFunction(Guid missionId, string role, IReadOnlyList<string> allowed, CancellationToken ct) =>
        AIFunctionFactory.Create(async ([System.ComponentModel.Description("Le nom de la compétence, tel qu'il est listé.")] string name) =>
        {
            var key = (name ?? "").Trim();
            if (!allowed.Contains(key) || await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill)
                return $"Compétence inconnue. Tes compétences : {string.Join(", ", allowed)}.";
            skill.Uses++;
            skill.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.AppendAsync(missionId, $"competence:{role}:{skill.Key}", ct);
            return skill.Body;
        }, AgentRoles.UseSkill, "Charge la procédure d'une de tes compétences. À appeler avant la tâche qu'elle décrit.");

    /// <summary>
    /// Apprendre (leçon 106) : l'agent propose une compétence ; la boucle s'arrête et demande l'accord d'un humain,
    /// qui voit le nom, la description et toute la procédure. Approuvée, elle appartient à qui a lancé la mission.
    /// </summary>
    private AIFunction ProposeSkillFunction(Guid missionId, string role, string owner, CancellationToken ct) =>
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(async (
            [System.ComponentModel.Description("Un nom court, 40 caractères au plus.")] string name,
            [System.ComponentModel.Description("Quand s'en servir, en une phrase.")] string description,
            [System.ComponentModel.Description("La procédure : étapes, format, règles.")] string body) =>
        {
            var (n, d, b) = ((name ?? "").Trim(), (description ?? "").Trim(), (body ?? "").Trim());
            if (SkillsEndpoints.Validate(n, d, b) is { Count: > 0 } errors)
                return $"Compétence non enregistrée : {string.Join(" ", errors.Values.SelectMany(v => v))}";
            var skill = await SkillLibrary.AddAsync(db, n, d, b, owner, hidden: false, by: $"agent:{role}", ct);
            _learned.Add(new AgentLearned(role, skill.Key));
            await audit.AppendAsync(missionId, $"competence-apprise:{role}:{skill.Key}", ct);
            return $"Compétence « {skill.Name} » enregistrée.";
        }, AgentRoles.ProposeSkill, "Propose de retenir une façon de faire comme compétence. Un humain doit l'accepter."));

    /// <summary>
    /// « action:analyste:read_file:src/Api.cs » : le rôle, l'outil et le chemin visé. Seulement le chemin, jamais le
    /// contenu (celui de write_file reste dans la mission) ; les caractères invisibles sont retirés et la longueur bornée.
    /// </summary>
    internal static string ActionEvent(string role, FunctionCallContent call)
    {
        // Le texte cherché (search), le chemin visé, ou le nom de la compétence (use_skill, propose_skill).
        var target = call.Name == "search" && call.Arguments?.TryGetValue("text", out var searched) == true ? searched?.ToString() ?? ""
            : call.Name == "run_command" && call.Arguments?.TryGetValue("command", out var command) == true ? command?.ToString() ?? ""
            : call.Arguments?.TryGetValue("path", out var value) == true || call.Arguments?.TryGetValue("name", out value) == true
                ? value?.ToString() ?? "" : "";
        // Par caractère Unicode complet (Rune) : une moitié de paire isolée devient U+FFFD (sinon PostgreSQL refuse le texte),
        // et l'on coupe sans jamais séparer une paire. On retire contrôle, format et séparateurs de ligne ou de paragraphe.
        var runes = target.EnumerateRunes()
            .Where(r => Rune.GetUnicodeCategory(r) is not (UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            .ToList();
        if (runes.Count > 120)
            runes = [new Rune('…'), .. runes[^119..]];
        var clean = string.Concat(runes.Select(r => r.ToString())).Trim();
        return $"action:{role}:{ToolName(call.Name)}:{(clean.Length == 0 ? "." : clean)}";
    }

    /// <summary>Le nom d'outil vient du modèle, qui peut en inventer un : seulement des minuscules et des « _ », sinon « ? ».</summary>
    internal static string ToolName(string name) =>
        name.Length is > 0 and <= 40 && name.All(c => c is (>= 'a' and <= 'z') or '_') ? name : "?";

    // On ajoute la décision humaine à chaque demande encore sans réponse.
    // ponytail: une seule décision pour toutes les demandes d'un même tour ; le client les affiche toutes avant.
    private static List<AIContent> AnswerPendingRequests(List<ChatMessage> history, bool approved)
    {
        var contents = history.SelectMany(m => m.Contents).ToList();
        var answered = contents.OfType<ToolApprovalResponseContent>().Select(r => r.RequestId).ToHashSet();
        return contents.OfType<ToolApprovalRequestContent>()
            .Where(request => !answered.Contains(request.RequestId))
            .Select(request => (AIContent)request.CreateResponse(approved))
            .ToList();
    }

    // Même montage que spikes/AgentLoop/FrameworkAgent.cs, plus la télémétrie : on règle nous-mêmes la limite de tours.
    private static AIAgent CreateAgent(AgentRole role, string instructions, IChatClient model, IReadOnlyList<AIFunction> tools, int maxIterations)
    {
        var withTools = model.AsBuilder()
            .UseFunctionInvocation(configure: invoker =>
            {
                invoker.MaximumIterationsPerRequest = maxIterations;
                // Les messages d'erreur de WorkspaceTools sont écrits pour le modèle et ne contiennent pas de secret.
                invoker.IncludeDetailedErrors = true;
            })
            // Une trace par appel au modèle (durée, modèle, tokens), sous la boucle : on voit chaque tour.
            // Sans le contenu des messages : il peut citer les fichiers lus.
            .UseOpenTelemetry(sourceName: Telemetry.SourceName, configure: otel => otel.EnableSensitiveData = false)
            .Build();

        return new ChatClientAgent(withTools, new ChatClientAgentOptions
        {
            Name = role.Key,
            ChatOptions = new ChatOptions { Instructions = instructions, Tools = [.. tools] },
            UseProvidedChatClientAsIs = true,
        })
            .AsBuilder()
            // Une trace parente par exécution de l'agent, qui regroupe tous ses appels au modèle.
            .UseOpenTelemetry(Telemetry.SourceName, otel => otel.EnableSensitiveData = false)
            .Build();
    }

    /// <summary>
    /// L'état d'une mission en pause : quel rôle travaillait, sa conversation, et les constats de l'Analyste (revue).
    /// Opaque pour le module Missions, qui le garde tel quel.
    /// </summary>
    private sealed record Stage(string Role, List<ChatMessage> History, string? Findings)
    {
        public string Serialize() => new JsonObject
        {
            ["role"] = Role,
            ["findings"] = Findings,
            ["history"] = JsonSerializer.SerializeToNode(History, Json),
        }.ToJsonString();

        public static Stage Parse(string json)
        {
            var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Conversation en pause illisible.");
            return new Stage(
                node["role"]?.GetValue<string>() ?? throw new InvalidOperationException("Rôle en pause absent."),
                node["history"].Deserialize<List<ChatMessage>>(Json) ?? [],
                node["findings"]?.GetValue<string>());
        }
    }
}
