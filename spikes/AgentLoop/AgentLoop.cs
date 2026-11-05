using Microsoft.Extensions.AI;

namespace AgentYattus.Spikes;

public enum AgentRunStatus
{
    Completed,
    MaxIterationsReached,
}

/// <param name="Iterations">Nombre d'appels au modèle.</param>
/// <param name="TotalTokens">Somme des tokens rapportés par le modèle sur toute l'exécution.</param>
public sealed record AgentRunResult(
    AgentRunStatus Status,
    string? FinalText,
    int Iterations,
    IReadOnlyList<ChatMessage> History,
    long TotalTokens);

/// <summary>
/// Boucle agentique écrite à la main : le modèle propose des appels d'outils, ce code décide de les exécuter.
/// </summary>
public sealed class AgentLoop(IChatClient chatClient, IReadOnlyList<AIFunction> tools, int maxIterations = 10)
{
    private readonly Dictionary<string, AIFunction> _toolsByName = tools.ToDictionary(tool => tool.Name);

    public async Task<AgentRunResult> RunAsync(string goal, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> history = [new(ChatRole.User, goal)];
        var options = new ChatOptions { Tools = [.. tools] };
        long totalTokens = 0;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var response = await chatClient.GetResponseAsync(history, options, cancellationToken);
            history.AddRange(response.Messages);
            totalTokens += response.Usage?.TotalTokenCount ?? 0;

            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
            if (calls.Count == 0)
            {
                return new AgentRunResult(AgentRunStatus.Completed, response.Text, iteration, history, totalTokens);
            }

            List<AIContent> results = [];
            foreach (var call in calls)
            {
                results.Add(new FunctionResultContent(call.CallId, await InvokeAsync(call, cancellationToken)));
            }

            history.Add(new ChatMessage(ChatRole.Tool, results));
        }

        return new AgentRunResult(AgentRunStatus.MaxIterationsReached, null, maxIterations, history, totalTokens);
    }

    // Une erreur d'outil devient un résultat texte : le modèle la lit et peut changer d'approche.
    // L'annulation, elle, n'est jamais attrapée : elle doit arrêter toute la boucle.
    private async Task<object?> InvokeAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        if (!_toolsByName.TryGetValue(call.Name, out var tool))
        {
            return $"Erreur : l'outil '{call.Name}' n'existe pas.";
        }

        try
        {
            return await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Erreur de l'outil '{call.Name}' : {ex.Message}";
        }
    }
}
