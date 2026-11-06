using System.Text.Json;
using System.Text.Json.Nodes;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Modules.Agents;

internal sealed class AgentsOptions
{
    public const string Section = "Agents";
    /// <summary>Dossier que l'agent a le droit de lire. Choisi côté serveur, jamais par le client.</summary>
    public string? Workspace { get; set; }

    public int MaxIterations { get; set; } = 10;
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
    ILlmPricing pricing) : IAgentRunner
{
    // Les types de Microsoft.Extensions.AI (messages, appels d'outils, demandes d'approbation) savent se sérialiser.
    private static readonly JsonSerializerOptions Json = AIJsonUtilities.DefaultOptions;

    public async Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        // Le dossier du projet (choisi par un administrateur de l'instance) ; null : celui de la configuration,
        // que MissionRunner n'autorise que pour les projets d'un administrateur et les missions antérieures aux projets.
        workspace ??= settings.Workspace;
        if (string.IsNullOrWhiteSpace(workspace))
            throw new InvalidOperationException(
                "Workspace absent. Lance : dotnet user-secrets set \"Agents:Workspace\" \"<dossier>\" --project src/AgentYattus.Api");
        var tools = new WorkspaceTools(workspace).AsAIFunctions();

        var usage = new AgentUsage(0, 0, 0);
        Stage stage;
        if (resume is not null)
        {
            stage = Stage.Parse(resume.Conversation);
            stage.History.Add(new ChatMessage(ChatRole.User, AnswerPendingRequests(stage.History, resume.Approved)));
        }
        else if (kind == MissionKind.Question)
        {
            stage = new Stage(AgentRoles.Analyst.Key, [new ChatMessage(ChatRole.User, goal)], Findings: null);
        }
        else
        {
            // Revue, étape 1 : l'Analyste explore et rend ses constats. Il n'a aucun outil d'écriture : il ne s'arrête jamais.
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Analyst.Key}", cancellationToken);
            var (analysis, analysisUsage) = await RunRoleAsync(missionId, AgentRoles.Analyst, tools, settings.MaxIterations,
                [new ChatMessage(ChatRole.User, AgentRoles.ReviewBrief(goal))], cancellationToken);
            usage += analysisUsage;
            var findings = analysis.Text;

            // Étape 2, le passage de relais : le Rédacteur part des constats, pas de zéro.
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Writer.Key}", cancellationToken);
            stage = new Stage(AgentRoles.Writer.Key, [new ChatMessage(ChatRole.User, AgentRoles.Handoff(goal, findings))], findings);
        }

        var role = AgentRoles.All.Single(r => r.Key == stage.Role);
        var (response, roleUsage) = await RunRoleAsync(missionId, role, tools, settings.MaxIterations, stage.History, cancellationToken);
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
            return new AgentOutcome(null, new AgentApproval(calls), stage.Serialize(), usage);
        }

        // Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
        logger.LogInformation(
            "Mission {MissionId} ({Role}) : {Tokens} tokens, réponse de {Length} caractères",
            missionId, role.Key, response.Usage?.TotalTokenCount, response.Text.Length);
        return new AgentOutcome(stage.Findings is null
            ? response.Text
            : $"{response.Text}\n\n---\nConstats de l'Analyste :\n{stage.Findings}", Usage: usage);
    }

    private async Task<(AgentResponse Response, AgentUsage Usage)> RunRoleAsync(
        Guid missionId, AgentRole role, IReadOnlyList<AIFunction> allTools, int maxIterations,
        List<ChatMessage> history, CancellationToken cancellationToken)
    {
        // Chaque rôle a son modèle : le sien s'il en a un, sinon celui par défaut.
        var client = await llm.GetAsync(role.Key, cancellationToken) ?? throw new InvalidOperationException(
            "Aucun modèle : connecte un fournisseur dans Paramètres › Modèles, ou lance " +
            "dotnet user-secrets set \"Anthropic:ApiKey\" \"<ta-clé>\" --project src/AgentYattus.Api");
        await audit.AppendAsync(missionId, $"modele:{client.Model}", cancellationToken);

        // Chaque rôle ne reçoit que ses outils : l'Analyste ne peut pas écrire, même s'il le demandait.
        var tools = allTools.Where(t => role.Tools.Contains(t.Name)).ToList();
        var agent = CreateAgent(role, client.Client, tools, maxIterations);
        var response = await agent.RunAsync(history, cancellationToken: cancellationToken);

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
                await audit.AppendAsync(missionId, $"outil:{call.Name}", cancellationToken);
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
    private static AIAgent CreateAgent(AgentRole role, IChatClient model, IReadOnlyList<AIFunction> tools, int maxIterations)
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
            ChatOptions = new ChatOptions { Instructions = role.Instructions, Tools = [.. tools] },
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
