using System.Text.Json;
using AgentYattus.Api.Modules.Audit;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Modules.Agents;

internal sealed class AgentsOptions
{
    public const string Section = "Agents";
    public const string DefaultModel = "claude-haiku-5-5";

    public string Model { get; set; } = DefaultModel;

    /// <summary>Dossier que l'agent a le droit de lire. Choisi côté serveur, jamais par le client.</summary>
    public string? Workspace { get; set; }

    public int MaxIterations { get; set; } = 10;
}

// Dépendances inter-modules en paramètres du constructeur : le test d'architecture les voit.
internal sealed class AgentFrameworkRunner(
    IOptions<AgentsOptions> options,
    AuditLog audit,
    ILogger<AgentFrameworkRunner> logger,
    IChatClient? chatClient = null) : IAgentRunner
{
    private const string Instructions =
        "Tu es un agent d'analyse de code. Tu explores le workspace avec les outils list_files et read_file, " +
        "puis tu réponds en français, de façon concise, en citant les fichiers sur lesquels tu t'appuies. " +
        "Si on te demande d'écrire un fichier, utilise write_file : un humain approuvera ou refusera l'écriture.";

    // Les types de Microsoft.Extensions.AI (messages, appels d'outils, demandes d'approbation) savent se sérialiser.
    private static readonly JsonSerializerOptions Json = AIJsonUtilities.DefaultOptions;

    public async Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, AgentResume? resume, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var model = chatClient ?? throw new InvalidOperationException(
            "Clé d'API absente. Lance : dotnet user-secrets set \"Anthropic:ApiKey\" \"<ta-clé>\" --project src/AgentYattus.Api");
        if (string.IsNullOrWhiteSpace(settings.Workspace))
            throw new InvalidOperationException(
                "Workspace absent. Lance : dotnet user-secrets set \"Agents:Workspace\" \"<dossier>\" --project src/AgentYattus.Api");

        var history = resume is null ? [new ChatMessage(ChatRole.User, goal)] : Resume(resume);
        var tools = new WorkspaceTools(settings.Workspace);
        var agent = CreateAgent(model, tools.AsAIFunctions(), settings.MaxIterations);
        var response = await agent.RunAsync(history, cancellationToken: cancellationToken);

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
        if (toolTurns >= settings.MaxIterations)
            await audit.AppendAsync(missionId, "limite-de-tours", cancellationToken);

        var requests = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
        if (requests.Count > 0)
        {
            // La boucle s'est arrêtée avant l'action sensible. On garde toute la conversation pour la reprendre.
            history.AddRange(response.Messages);
            var calls = requests.Select(r => r.ToolCall).OfType<FunctionCallContent>()
                .Select(c => new AgentToolCall(c.Name, JsonSerializer.Serialize(c.Arguments, Json)))
                .ToList();
            foreach (var call in calls)
                await audit.AppendAsync(missionId, $"approbation-demandee:{call.Tool}", cancellationToken);
            return new AgentOutcome(null, new AgentApproval(calls), JsonSerializer.Serialize(history, Json));
        }

        // Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
        logger.LogInformation(
            "Mission {MissionId} : {Tokens} tokens, réponse de {Length} caractères",
            missionId, response.Usage?.TotalTokenCount, response.Text.Length);
        return new AgentOutcome(response.Text);
    }

    // On rejoue la conversation, suivie de la décision humaine pour chaque demande en attente.
    // ponytail: une seule décision pour toutes les demandes d'un même tour ; le client les affiche toutes avant.
    private static List<ChatMessage> Resume(AgentResume resume)
    {
        var history = JsonSerializer.Deserialize<List<ChatMessage>>(resume.Conversation, Json)
            ?? throw new InvalidOperationException("Conversation en pause illisible.");
        var contents = history.SelectMany(m => m.Contents).ToList();
        var answered = contents.OfType<ToolApprovalResponseContent>().Select(r => r.RequestId).ToHashSet();
        var pending = contents.OfType<ToolApprovalRequestContent>()
            .Where(request => !answered.Contains(request.RequestId))
            .Select(request => (AIContent)request.CreateResponse(resume.Approved))
            .ToList();
        history.Add(new ChatMessage(ChatRole.User, pending));
        return history;
    }

    // Même montage que spikes/AgentLoop/FrameworkAgent.cs, plus la télémétrie : on règle nous-mêmes la limite de tours.
    private static AIAgent CreateAgent(IChatClient model, IReadOnlyList<AIFunction> tools, int maxIterations)
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
            Name = "analyste",
            ChatOptions = new ChatOptions { Instructions = Instructions, Tools = [.. tools] },
            UseProvidedChatClientAsIs = true,
        })
            .AsBuilder()
            // Une trace parente par exécution de l'agent, qui regroupe tous ses appels au modèle.
            .UseOpenTelemetry(Telemetry.SourceName, otel => otel.EnableSensitiveData = false)
            .Build();
    }
}
