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

    public TokenEconomyOptions TokenEconomy { get; set; } = new();

    // Le bac à sable des commandes (ADR 0015).
    public string SandboxImage { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";
    public int SandboxTimeoutSeconds { get; set; } = 120;

    /// <summary>« none » (par défaut) : rien ne sort du conteneur ; « bridge » : réseau ouvert, décidé par l'administrateur.</summary>
    public string SandboxNetwork { get; set; } = "none";

    /// <summary>Les sites que read_documentation peut lire (leçon 133) ; vide : la liste officielle par défaut.</summary>
    public List<string> DocumentationHosts { get; set; } = [];

    public IReadOnlyList<string> DocumentationHostList =>
        DocumentationHosts.Count > 0 ? [.. DocumentationHosts.Select(h => h.Trim().ToLowerInvariant())] : DocumentationReader.DefaultHosts;

    /// <summary>Où ranger les copies de travail des missions sur branche (leçon 127) ; par défaut, le dossier de données.</summary>
    public string? WorktreesFolder { get; set; }

    public string WorktreesRoot => WorktreesFolder is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentYattus", "worktrees");
}

/// <summary>
/// Exécute une mission avec Agent Framework.
/// Question : l'agent choisi (le chef d'équipe peut passer la main, leçon 129), sinon l'Analyste. Revue : l'Analyste rend ses constats, puis le Rédacteur écrit REPORT.md (soumis à approbation).
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
    ICommandSandbox sandbox,
    DocumentationReader docs,
    AgentLiveContext live) : IAgentRunner
{
    // Les types de Microsoft.Extensions.AI (messages, appels d'outils, demandes d'approbation) savent se sérialiser.
    private static readonly JsonSerializerOptions Json = AIJsonUtilities.DefaultOptions;

    // Les compétences approuvées pendant cette exécution (propose_skill) : MissionRunner les range sur le collègue.
    private readonly List<AgentLearned> _learned = [];

    // Le collègue à qui le chef d'équipe a confié la demande (leçon 129), et sa consigne ; null tant qu'il n'a pas passé la main.
    private (string Key, string Brief)? _handoff;

    // Le dossier est la copie de travail d'une mission sur branche (ADR 0016) : écrire et lancer une commande n'y demandent pas d'accord.
    private bool _isolated;
    private string? _question;

    // Les compétences choisies : un index ; les procédures se chargent avec use_skill.
    private IReadOnlyList<Skill> _chosen = [];

    // Ce que la mission a réellement utilisé, pour le citer (leçons 132 et 133) : compétences (version) et fichiers lus.
    private readonly List<(string Key, string Name, int Version, string How)> _skillsUsed = [];
    private readonly List<string> _filesRead = [];

    private readonly MissionTokenBudget _tokens = new(options.Value.TokenEconomy.MaxMissionTokens);

    public async Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace,
        IReadOnlyDictionary<string, AgentSeat>? seats, string? agent, string? launchedBy, CancellationToken cancellationToken,
        Func<string, Task>? onText = null, IReadOnlyList<string>? skills = null)
    {
        var settings = options.Value;
        _tokens.Reset(resume?.SpentTokens ?? 0);
        if (kind == MissionKind.Recruitment)
            return await RecruitAsync(missionId, goal, resume, launchedBy, cancellationToken, onText);
        await LoadChosenSkillsAsync(missionId, skills, launchedBy, firstRun: resume is null, cancellationToken);
        // Les réglages de l'instance (admin › Réglages) l'emportent sur la configuration de départ (ADR 0010).
        var tuning = await instance.GetAsync(cancellationToken);
        // Limite de tours : celle du rôle (ADR 0011), sinon celle de l'instance, sinon la configuration.
        var instanceMax = tuning.MaxIterations ?? settings.MaxIterations;
        // Le dossier du projet (choisi par un administrateur de l'instance) ; null : celui de la configuration,
        // que MissionRunner n'autorise que pour les projets d'un administrateur et les missions antérieures aux projets.
        workspace ??= settings.Workspace;
        _isolated = !string.IsNullOrWhiteSpace(workspace) && IsMissionWorktree(workspace, settings.WorktreesRoot);
        // Sans dossier, la conversation reste disponible, sans accès aux fichiers ni aux commandes.
        var files = string.IsNullOrWhiteSpace(workspace) ? null
            : new WorkspaceTools(workspace, tuning.MaxFileKb * 1024L, tuning.ExcludedFiles, sandbox, _isolated);
        // La documentation (leçon 133) ne dépend pas du dossier : une conversation sans dossier peut aussi la consulter.
        var readDocs = AIFunctionFactory.Create(docs.ReadAsync, DocumentationReader.ToolName, DocumentationReader.Description);
        IReadOnlyList<AIFunction> tools = files is null ? [readDocs] :
        [
            .. files.AsAIFunctions(),
            .. new GitTools(workspace!, tuning.MaxFileKb * 1024).AsAIFunctions(),
            readDocs,
        ];

        // Développer (ADR 0016) : des étapes tenues par le code. Une réponse de l'humain ensuite continue avec le Développeur.
        if (kind == MissionKind.Develop && resume is null)
        {
            if (files is null || !_isolated)
                throw new MissionBlockedException("Une mission Développer travaille dans sa propre copie de travail : le mode branche est obligatoire.");
            return await DevelopAsync(missionId, goal, workspace!, files, tools, instanceMax, seats, launchedBy, cancellationToken, onText);
        }

        var usage = new AgentUsage(0, 0, 0);
        Stage stage;
        if (resume is not null)
        {
            stage = Stage.Parse(resume.Conversation);
            // Une conversation directe garde son destinataire, même pour un ancien historique délégué.
            if (kind == MissionKind.Question && resume.FollowUp is not null && stage.Role != (agent ?? AgentRoles.Analyst.Key))
                stage = new Stage(agent ?? AgentRoles.Analyst.Key, stage.History, stage.Findings);
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
            if (_question is not null)
            {
                var waiting = new List<ChatMessage> { new(ChatRole.User, AgentRoles.ReviewBrief(goal)) };
                waiting.AddRange(analysis.Messages); waiting.Add(new(ChatRole.Assistant, _question));
                return new AgentOutcome(_question, Conversation: new Stage(AgentRoles.Analyst.Key, waiting, null).Serialize(),
                    Usage: usage, AuthorRole: AgentRoles.Analyst.Key, NeedsReply: true);
            }
            var findings = analysis.Text;

            // Étape 2, le passage de relais : le Rédacteur part des constats, pas de zéro.
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Writer.Key}", cancellationToken);
            stage = new Stage(AgentRoles.Writer.Key, [new ChatMessage(ChatRole.User, AgentRoles.Handoff(goal, findings))], findings);
        }

        // Le chef d'équipe (leçon 129), au départ comme quand on lui répond : un collègue recruté, désigné au recrutement.
        // Il travaille avec sa consigne et ses outils, et peut en plus confier une question à un collègue du projet.
        // Une revue suit son circuit (Analyste puis Rédacteur) : personne n'y répartit le travail.
        var team = kind == MissionKind.Question && seats?.GetValueOrDefault(stage.Role) is { Leader: true }
            ? await TeamAsync(seats, stage.Role, launchedBy, cancellationToken)
            : [];
        var asked = stage.History.LastOrDefault(m => m.Role == ChatRole.User && !string.IsNullOrWhiteSpace(m.Text))?.Text ?? goal;

        // Même retiré entre-temps, l'agent d'une mission en pause la termine : la décision humaine reste possible.
        var role = await catalog.FindAsync(stage.Role, cancellationToken)
                   ?? throw new MissionBlockedException($"L'agent « {stage.Role} » n'existe plus.");
        var (response, roleUsage) = await RunRoleAsync(missionId, role, tools, instanceMax, stage.History, seats, launchedBy, cancellationToken, onText, team);
        usage += roleUsage;

        if (kind == MissionKind.Review && stage.Role == AgentRoles.Analyst.Key && _question is null
            && !response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Any())
        {
            var findings = response.Text;
            role = AgentRoles.Writer;
            stage = new Stage(role.Key, [new ChatMessage(ChatRole.User, AgentRoles.Handoff(goal, findings))], findings);
            (response, roleUsage) = await RunRoleAsync(missionId, role, tools, instanceMax, stage.History, seats, launchedBy, cancellationToken, onText);
            usage += roleUsage;
        }
        List<AgentContribution> interventions = [];
        List<ChatMessage>? delegatedHistory = null;
        if (_handoff is { } handoff)
        {
            var (chosen, brief) = handoff;
            await audit.AppendAsync(missionId, $"delegue:{chosen}", cancellationToken);
            if (chosen == RecruitmentAssistant.Role.Key)
            {
                stage.History.AddRange(response.Messages);
                return new AgentOutcome("J’ai transmis votre demande à l’agent RH. Vous pouvez suivre le recrutement et répondre à ses questions.",
                    Conversation: stage.Serialize(), Usage: usage, Learned: _learned,
                    RecruitmentBrief: AgentRoles.Delegation(asked, brief), AuthorRole: stage.Role);
            }
            // Le groupe conserve aussi la prise de parole du chef avant la contribution du spécialiste.
            if (!string.IsNullOrWhiteSpace(response.Text)) interventions.Add(new(stage.Role, response.Text));
            // Le collègue repart de la demande et de la consigne : la suite (accords, réponses) se fait avec lui seul.
            stage.History.AddRange(response.Messages);
            delegatedHistory = new List<ChatMessage> { new(ChatRole.User, AgentRoles.Delegation(asked, brief)) };
            role = await catalog.FindAsync(chosen, cancellationToken)
                   ?? throw new MissionBlockedException($"L'agent « {chosen} » n'existe plus.");
            (response, roleUsage) = await RunRoleAsync(missionId, role, tools, instanceMax, delegatedHistory, seats, launchedBy, cancellationToken, onText);
            usage += roleUsage;
        }

        var requests = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
        if (requests.Count > 0)
        {
            if (delegatedHistory is not null)
                stage = new Stage(role.Key, delegatedHistory, null);
            // La boucle s'est arrêtée avant l'action sensible. On garde de quoi reprendre exactement ici.
            stage.History.AddRange(response.Messages);
            var calls = requests.Select(r => r.ToolCall).OfType<FunctionCallContent>()
                .Select(c => new AgentToolCall(c.Name, JsonSerializer.Serialize(c.Arguments, Json)))
                .ToList();
            foreach (var call in calls)
                await audit.AppendAsync(missionId, $"approbation-demandee:{call.Tool}", cancellationToken);
            return new AgentOutcome(null, new AgentApproval(calls), stage.Serialize(), usage, _learned, Contributions: interventions.Count > 0 ? interventions : null);
        }

        // Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
        logger.LogInformation(
            "Mission {MissionId} ({Role}) : {Tokens} tokens, réponse de {Length} caractères",
            missionId, role.Key, response.Usage?.TotalTokenCount, response.Text.Length);
        // La conversation complète est gardée : on pourra répondre à l'agent (leçon 117). Les constats ne sont
        // ajoutés qu'à la première réponse d'une revue.
        if (_handoff is { } forwarded && forwarded.Key != RecruitmentAssistant.Role.Key)
            stage.History.Add(new ChatMessage(ChatRole.Assistant, $"Contribution de {role.Name} :\n{_question ?? response.Text}"));
        else
            stage.History.AddRange(response.Messages);
        if (_question is not null)
            stage.History.Add(new ChatMessage(ChatRole.Assistant, _question));
        var answer = _question ?? (stage.Findings is null || resume?.FollowUp is not null
            ? response.Text
            : $"{response.Text}\n\n---\nConstats de l'Analyste :\n{stage.Findings}");
        return new AgentOutcome(answer, Conversation: stage.Serialize(), Usage: usage, Learned: _learned,
            AuthorRole: role.Key, NeedsReply: _question is not null,
            Contributions: interventions.Count > 0 ? [.. interventions, new(role.Key, _question ?? response.Text)]
                : stage.Findings is not null && resume is null && _question is null
                ? [new(AgentRoles.Analyst.Key, stage.Findings), new(role.Key, response.Text)] : null);
    }

    private async Task<AgentOutcome> RecruitAsync(Guid missionId, string goal, AgentResume? resume, string? launchedBy,
        CancellationToken ct, Func<string, Task>? onText)
    {
        var stage = resume is null ? new Stage("_rh", [new ChatMessage(ChatRole.User, goal)], null) : Stage.Parse(resume.Conversation);
        if (resume is not null)
            stage.History.Add(new ChatMessage(ChatRole.User, resume.FollowUp ?? "Continue l'entretien de recrutement."));
        RecruitmentDraftDto? draft = null;
        var prepare = AIFunctionFactory.Create((RecruitmentDraftDto profile) =>
        {
            draft = null;
            if (RecruitmentAssistant.Validate(profile) is { } error)
                return $"Fiche refusée : {error} Corrige-la et rappelle cet outil.";
            draft = profile;
            return "Fiche prête à afficher. Aucun agent n'est encore créé : la personne peut la relire et lancer la création depuis le recrutement.";
        }, RecruitmentAssistant.Tool, "Prépare la fiche complète d'un agent logiciel après clarification du besoin. Ne crée rien.");
        var role = RecruitmentAssistant.Role with
        {
            Instructions = RecruitmentAssistant.Role.Instructions + "\n\nCatalogue des seuls outils disponibles pour le futur agent :\n"
                + string.Join('\n', AgentsModule.AllTools.Select(t => $"- {t.Name} : {t.Description}")),
        };
        await audit.AppendAsync(missionId, "agent:rh", ct);
        var tuning = await instance.GetAsync(ct);
        var (response, usage) = await RunRoleAsync(missionId, role, [prepare], tuning.MaxIterations ?? options.Value.MaxIterations,
            stage.History, seats: null, launchedBy, ct, onText, allowLearning: false);
        stage.History.AddRange(response.Messages);
        var message = _question ?? (string.IsNullOrWhiteSpace(response.Text)
            ? draft is null ? "Pouvez-vous préciser le résultat que vous attendez de cet agent ?" : "La fiche est prête : vous pouvez créer l'agent ou ajuster le formulaire."
            : response.Text);
        return new AgentOutcome(JsonSerializer.Serialize(new RecruitmentResultDto(message, draft, RequestId: draft is null ? null : Guid.CreateVersion7()), JsonSerializerOptions.Web),
            Conversation: stage.Serialize(), Usage: usage, AuthorRole: "_rh", NeedsReply: _question is not null);
    }

    /// <summary>
    /// À qui le chef peut confier la demande : les autres collègues recrutés dans le projet, s'ils occupent un poste du code ou
    /// un agent créé que voit qui a lancé la mission (ceux de l'instance, et les siens).
    /// </summary>
    private async Task<IReadOnlyList<AgentRole>> TeamAsync(
        IReadOnlyDictionary<string, AgentSeat> seats, string leader, string? launchedBy, CancellationToken ct)
    {
        var custom = await db.CustomRoles.AsNoTracking().Where(r => r.Active && (r.OwnerId == null || r.OwnerId == launchedBy)).ToListAsync(ct);
        return [.. AgentRoles.All.Concat(custom.OrderBy(r => r.CreatedAt).Select(AgentCatalog.ToRole))
            .Where(r => r.Key != leader && seats.ContainsKey(r.Key)), RecruitmentAssistant.Role];
    }

    /// <summary>
    /// « confier » : le chef choisit un collègue de son équipe. La boucle du chef s'arrête aussitôt (pas de tour de plus) ;
    /// le collègue reprend ensuite la demande avec ses propres outils (le passage de relais, leçon 128).
    /// </summary>
    private AIFunction DelegateFunction(IReadOnlyList<AgentRole> team) =>
        AIFunctionFactory.Create(
            ([System.ComponentModel.Description("La clé du collègue, telle qu'elle est listée.")] string collegue,
             [System.ComponentModel.Description("Ce que tu lui demandes, clair et complet.")] string consigne) =>
            {
                var key = (collegue ?? "").Trim();
                if (team.All(r => r.Key != key))
                    return $"Collègue inconnu. Ton équipe : {string.Join(", ", team.Select(r => r.Key))}.";
                _handoff ??= (key, (consigne ?? "").Trim());
                if (FunctionInvokingChatClient.CurrentContext is { } invocation)
                    invocation.Terminate = true;
                return "C'est transmis : ton collègue répond directement.";
            }, AgentRoles.Delegate, "Confie la demande à un collègue, qui répondra directement à la personne.");

    /// <summary>Le dossier est-il une copie de travail de mission (mode branche) ? Seul le code la crée, sous WorktreesRoot.</summary>
    internal static bool IsMissionWorktree(string workspace, string worktreesRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreesRoot)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(workspace).StartsWith(root, StringComparison.Ordinal) && LibGit2Sharp.Repository.IsValid(workspace);
    }

    /// <summary>
    /// La mission Développer (ADR 0016), étape par étape, tenue par le code : profil du dépôt ; contrôles au départ ; le Développeur
    /// fixe ses critères, modifie et vérifie ; le code relance les contrôles ; le Relecteur juge le diff ; une correction au plus.
    /// Les contrôles sont lancés par le code : ce qu'on affiche a vraiment tourné.
    /// </summary>
    private async Task<AgentOutcome> DevelopAsync(
        Guid missionId, string goal, string workspace, WorkspaceTools files, IReadOnlyList<AIFunction> tools, int instanceMax,
        IReadOnlyDictionary<string, AgentSeat>? seats, string? launchedBy, CancellationToken cancellationToken, Func<string, Task>? onText)
    {
        var usage = new AgentUsage(0, 0, 0);
        var profile = files.Profile(cancellationToken);
        // Au départ, seulement sur une copie intacte : après une reprise, elle porte déjà des changements, et « avant » mentirait.
        var before = GitBranches.IsClean(workspace) ? await RunChecksAsync(missionId, "avant", files, profile, cancellationToken) : null;
        // Pendant les étapes, pas de commit (il attend un accord) : il viendra après, si l'humain le demande.
        var developerTools = tools.Where(t => t.Name != "git_commit").ToList();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, AgentRoles.DevelopBrief(goal, profile.Describe(), DevelopChecks.Table(profile.Checks, before, null))),
        };
        var report = "";
        string? review = null;
        var verdict = ReviewVerdict.Inconclusive;
        IReadOnlyList<CheckRun> after = [];
        var rounds = 0;
        while (rounds < DevelopChecks.MaxRounds)
        {
            rounds++;
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Developer.Key}", cancellationToken);
            var (developed, developUsage) = await RunRoleAsync(missionId, AgentRoles.Developer, developerTools, instanceMax, history, seats,
                launchedBy, cancellationToken, onText, allowLearning: false);
            usage += developUsage;
            history.AddRange(developed.Messages);
            report = developed.Text;

            after = await RunChecksAsync(missionId, "apres", files, profile, cancellationToken);
            var table = DevelopChecks.Table(profile.Checks, before, after);
            var diff = GitBranches.Changes(workspace, DevelopChecks.MaxDiffChars).Diff;
            await audit.AppendAsync(missionId, $"agent:{AgentRoles.Reviewer.Key}", cancellationToken);
            var (reviewed, reviewUsage) = await RunRoleAsync(missionId, AgentRoles.Reviewer, tools, instanceMax,
                [new ChatMessage(ChatRole.User, AgentRoles.ReviewDevelopment(goal, report, table, diff))], seats, launchedBy, cancellationToken,
                allowLearning: false);
            usage += reviewUsage;
            review = reviewed.Text;
            verdict = DevelopChecks.Verdict(review);
            await audit.AppendAsync(missionId, verdict switch
            {
                ReviewVerdict.Accepted => "relecture:acceptee",
                ReviewVerdict.ChangesRequested => "relecture:a-corriger",
                _ => "relecture:non-concluante",
            }, cancellationToken);
            if (verdict != ReviewVerdict.ChangesRequested)
                break;
            if (rounds < DevelopChecks.MaxRounds)
                history.Add(new ChatMessage(ChatRole.User, AgentRoles.Corrections(review, table)));
        }

        var answer = DevelopChecks.Compose(report, profile, before, after, verdict, review, rounds,
            GitBranches.Changes(workspace, DevelopChecks.MaxDiffChars),
            DevelopChecks.Provenance(_skillsUsed, docs.Consulted, profile.Instructions.Select(i => i.Path).ToList(), _filesRead));
        // La conversation gardée est celle du Développeur : répondre à la mission, c'est lui répondre (corriger, commiter).
        return new AgentOutcome(answer, Conversation: new Stage(AgentRoles.Developer.Key, history, Findings: null).Serialize(), Usage: usage,
            Learned: _learned);
    }

    /// <summary>Les contrôles du dépôt, lancés par le code dans le bac à sable ; sans bac à sable, aucun ne tourne.</summary>
    private async Task<IReadOnlyList<CheckRun>> RunChecksAsync(
        Guid missionId, string phase, WorkspaceTools files, RepositoryProfile profile, CancellationToken cancellationToken)
    {
        var runs = new List<CheckRun>();
        foreach (var check in profile.Checks)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CommandResult? result;
            try
            {
                // Le bac à sable absent pour le premier : inutile d'essayer les suivants.
                result = runs.Count > 0 && runs[0].Outcome == CheckOutcome.Unavailable && runs[0].Note.StartsWith("bac à sable", StringComparison.Ordinal)
                    ? null : await files.ExecuteAsync(check.Command, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                // Le dossier ne peut pas être monté sans risque (trop de fichiers exclus, trop d'entrées) : la commande est refusée.
                result = new CommandResult(-1, ex.Message, TimedOut: false);
            }
            var run = DevelopChecks.Classify(check, result, clock.Elapsed);
            runs.Add(run);
            await audit.AppendAsync(missionId, $"controle:{phase}:{check.Name}:{run.Outcome.ToString().ToLowerInvariant()}", cancellationToken);
        }
        return runs;
    }

    /// <param name="team">Pour le chef d'équipe : les collègues à qui il peut confier la demande ; vide pour les autres.</param>
    /// <param name="allowLearning">Faux pendant les étapes d'une mission Développer : proposer une compétence la mettrait en pause.</param>
    private async Task<(AgentResponse Response, AgentUsage Usage)> RunRoleAsync(
        Guid missionId, AgentRole role, IReadOnlyList<AIFunction> allTools, int instanceMax,
        List<ChatMessage> history, IReadOnlyDictionary<string, AgentSeat>? seats, string? launchedBy, CancellationToken cancellationToken,
        Func<string, Task>? onText = null, IReadOnlyList<AgentRole>? team = null, bool allowLearning = true)
    {
        // La consigne et la limite en vigueur pour ce rôle ; les garde-fous sont toujours ajoutés par le code.
        _question = null;
        await live.StartAsync(role.Key);
        var definition = await definitions.CurrentAsync(role.Key, cancellationToken);
        var maxIterations = definition?.MaxIterations ?? instanceMax;
        var seat = seats?.GetValueOrDefault(role.Key);
        // Les compétences du collègue (leçon 104) ; une compétence retirée depuis n'est plus chargée.
        // Dans un projet partagé, celle d'Alice ne sert qu'aux missions d'Alice : Bob ne la lit pas en la faisant réciter à l'agent.
        var keys = seat?.Skills ?? [];
        var skills = keys.Count == 0 ? [] : await db.Skills.AsNoTracking()
            .Where(s => keys.Contains(s.Key) && s.Active && s.Status != SkillStatus.Draft && (s.OwnerId == null || s.OwnerId == launchedBy))
            .OrderBy(s => s.Key).ToListAsync(cancellationToken);
        // Apprendre suppose un collègue sur qui ranger la compétence, et quelqu'un à qui elle appartiendra.
        var learn = allowLearning && seat is not null && launchedBy is not null;
        var instructions = AgentRoles.Compose(role, definition?.Instructions, seat?.Style, [.. skills.Select(s => (s.Key, s.Description))], learn, team, _isolated,
            [.. _chosen.Select(s => (s.Key, s.Version, s.Description))], questions: allowLearning || role.Key == "_rh");

        // Sans dossier : la documentation reste lisible (leçon 133), mais ni fichiers, ni git, ni commandes.
        if (!allTools.Any(t => t.Name == "read_file"))
            instructions += "\nAucun dossier de travail n'est configuré pour cette conversation. Réponds normalement aux questions. " +
                "Tu n'as accès ni aux fichiers, ni à git, ni aux commandes. Si la demande nécessite ces accès, " +
                "invite la personne à choisir un dossier de travail pour ce projet depuis l'accueil (bouton dossier). " +
                "Ne prétends pas avoir consulté ou modifié des fichiers.";

        // Chaque rôle a son modèle : le sien s'il en a un, sinon celui par défaut.
        // Les clés de qui a lancé la mission passent avant celles de l'instance (leçon 134) ; celles d'un autre, jamais.
        var client = await llm.GetAsync(role.Key, launchedBy, cancellationToken) ?? throw new MissionBlockedException(
            "Aucun modèle n'est connecté : ajoutez un fournisseur et sa clé dans « Modèles et clés » (ou, sur un serveur, " +
            "dans l'espace admin › Modèles), puis relancez la mission.");
        await audit.AppendAsync(missionId, $"modele:{client.Model}", cancellationToken);
        await audit.AppendAsync(missionId, client.Personal ? "cle:personnelle" : "cle:instance", cancellationToken);

        // Chaque rôle ne reçoit que ses outils : l'Analyste ne peut pas écrire, même s'il le demandait.
        var tools = allTools.Where(t => role.Tools.Contains(t.Name)).ToList();
        var usableSkills = skills.Select(s => s.Key).Concat(_chosen.Select(s => s.Key)).Distinct().ToList();
        if (usableSkills.Count > 0)
            tools.Add(UseSkillFunction(missionId, role.Key, usableSkills, cancellationToken));
        if (learn)
            tools.Add(ProposeSkillFunction(missionId, role.Key, launchedBy!, cancellationToken));
        if (team is { Count: > 0 })
            tools.Add(DelegateFunction(team));
        if (allowLearning || role.Key == "_rh") tools.Add(AIFunctionFactory.Create((string question) =>
        {
            if (string.IsNullOrWhiteSpace(question) || question.Length > 2000)
                return "Posez une question courte, de 1 à 2 000 caractères.";
            _question = question.Trim();
            if (FunctionInvokingChatClient.CurrentContext is { } invocation)
                invocation.Terminate = true;
            return "La question est transmise à la personne. Attendre sa réponse.";
        }, "poser_question", "Demande une précision indispensable à la personne et attend sa réponse."));
        AgentUsage Price(long input, long output)
        {
            var cost = pricing.CostMicros(client.Model, input, output);
            return new AgentUsage(input, output, cost, client.Personal ? cost : 0);
        }
        var agent = CreateAgent(role, instructions,
            new TokenEconomyChatClient(client.Client, options.Value.TokenEconomy, _tokens, Price), tools, maxIterations);
        // En flux (leçon 119) : chaque morceau de texte part tout de suite vers qui regarde ; la réponse complète
        // (appels d'outils, demandes d'accord, tokens) est reconstituée ensuite à partir des mêmes morceaux.
        var updates = new List<AgentResponseUpdate>();
        // Le contexte du groupe reste borné et éphémère : il n'est pas recopié à chaque reprise dans l'historique.
        IEnumerable<ChatMessage> inputHistory = string.IsNullOrEmpty(live.GroupContext) ? history
            : new[] { new ChatMessage(ChatRole.User, live.GroupContext) }.Concat(history);
        await foreach (var update in agent.RunStreamingAsync(inputHistory, cancellationToken: cancellationToken))
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
                // Les fichiers lus : des sources de la mission, que le résultat d'une mission Développer cite.
                if (call.Name == "read_file" && call.Arguments?.TryGetValue("path", out var read) == true && read?.ToString() is { Length: > 0 } path
                    && _filesRead.Count < 50 && !_filesRead.Contains(path))
                    _filesRead.Add(path);
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
        var cost = pricing.CostMicros(client.Model, input, output);
        return (response, new AgentUsage(input, output, cost, client.Personal ? cost : 0));
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
            if (!allowed.Contains(key) || await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active && s.Status != SkillStatus.Draft, ct) is not { } skill)
                return $"Compétence inconnue. Tes compétences : {string.Join(", ", allowed)}.";
            skill.Uses++;
            skill.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.AppendAsync(missionId, $"competence:{role}:{skill.Key}", ct);
            // La version lue (leçon 132) : on saura ce que la mission a suivi, même si la compétence change ensuite.
            await audit.AppendAsync(missionId, $"version:competence:{skill.Key}:{skill.Version}", ct);
            if (!_skillsUsed.Any(u => u.Key == skill.Key))
                _skillsUsed.Add((skill.Key, skill.Name, skill.Version, "chargée par l'agent"));
            return skill.ForAgent();
        }, AgentRoles.UseSkill, "Charge la procédure d'une de tes compétences. À appeler avant la tâche qu'elle décrit.");

    /// <summary>
    /// Les compétences choisies par l'humain (leçon 132) : seulement celles qu'il peut utiliser (approuvées, de l'instance ou à lui).
    /// Au premier passage, chacune est comptée et entre dans le journal avec sa version ; une reprise les recharge sans les recompter.
    /// </summary>
    private async Task LoadChosenSkillsAsync(Guid missionId, IReadOnlyList<string>? keys, string? launchedBy, bool firstRun, CancellationToken ct)
    {
        if (keys is not { Count: > 0 })
            return;
        var rows = await db.Skills
            .Where(s => keys.Contains(s.Key) && s.Active && s.Status != SkillStatus.Draft && (s.OwnerId == null || s.OwnerId == launchedBy))
            .ToListAsync(ct);
        _chosen = [.. rows.OrderBy(s => keys.ToList().IndexOf(s.Key))];
        foreach (var skill in _chosen)
        {
            _skillsUsed.Add((skill.Key, skill.Name, skill.Version, "choisie par l'humain"));
            if (!firstRun)
                continue;
            skill.Uses++;
            skill.LastUsedAt = DateTimeOffset.UtcNow;
            await audit.AppendAsync(missionId, $"competence:choisie:{skill.Key}", ct);
            await audit.AppendAsync(missionId, $"version:competence:{skill.Key}:{skill.Version}", ct);
        }
        await db.SaveChangesAsync(ct);
    }

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
