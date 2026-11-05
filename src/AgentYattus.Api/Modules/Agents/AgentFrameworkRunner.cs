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
        "puis tu réponds en français, de façon concise, en citant les fichiers sur lesquels tu t'appuies.";

    public async Task<string?> RunAsync(Guid missionId, string goal, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var model = chatClient ?? throw new InvalidOperationException(
            "Clé d'API absente. Lance : dotnet user-secrets set \"Anthropic:ApiKey\" \"<ta-clé>\" --project src/AgentYattus.Api");
        if (string.IsNullOrWhiteSpace(settings.Workspace))
            throw new InvalidOperationException(
                "Workspace absent. Lance : dotnet user-secrets set \"Agents:Workspace\" \"<dossier>\" --project src/AgentYattus.Api");

        var tools = new WorkspaceTools(settings.Workspace);
        var agent = CreateAgent(model, tools.AsAIFunctions(), settings.MaxIterations);
        var response = await agent.RunAsync(goal, cancellationToken: cancellationToken);

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

        // Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
        logger.LogInformation(
            "Mission {MissionId} : {Tokens} tokens, réponse de {Length} caractères",
            missionId, response.Usage?.TotalTokenCount, response.Text.Length);
        return response.Text;
    }

    // Même montage que spikes/AgentLoop/FrameworkAgent.cs : on règle nous-mêmes la limite de tours.
    private static ChatClientAgent CreateAgent(IChatClient model, IReadOnlyList<AIFunction> tools, int maxIterations)
    {
        var withTools = model.AsBuilder()
            .UseFunctionInvocation(configure: invoker =>
            {
                invoker.MaximumIterationsPerRequest = maxIterations;
                // Les messages d'erreur de WorkspaceTools sont écrits pour le modèle et ne contiennent pas de secret.
                invoker.IncludeDetailedErrors = true;
            })
            .Build();

        return new ChatClientAgent(withTools, new ChatClientAgentOptions
        {
            Name = "analyste",
            ChatOptions = new ChatOptions { Instructions = Instructions, Tools = [.. tools] },
            UseProvidedChatClientAsIs = true,
        });
    }
}
