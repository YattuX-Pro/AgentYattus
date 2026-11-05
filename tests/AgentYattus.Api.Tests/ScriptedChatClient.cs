using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Tests;

/// <summary>
/// Faux IChatClient : renvoie des réponses écrites d'avance et enregistre chaque requête reçue.
/// Il rend la boucle testable sans réseau, sans coût et de façon déterministe.
/// </summary>
public sealed class ScriptedChatClient(params ChatResponse[] script) : IChatClient
{
    private readonly Queue<ChatResponse> _script = new(script);

    /// <summary>Copie de l'historique reçu à chaque appel, dans l'ordre.</summary>
    public List<List<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions?> Options { get; } = [];

    /// <summary>Appelé après l'enregistrement d'une requête, avec son index.</summary>
    public Action<int>? OnRequest { get; init; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add([.. messages]);
        Options.Add(options);
        OnRequest?.Invoke(Requests.Count - 1);

        return _script.Count > 0
            ? Task.FromResult(_script.Dequeue())
            : throw new InvalidOperationException("Script épuisé : le modèle a été appelé plus de fois que prévu.");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponse Text(string text, long tokens = 0) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { TotalTokenCount = tokens } };

    public static ChatResponse ToolCall(string name, Dictionary<string, object?>? arguments = null, string callId = "call_1", long tokens = 0) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]))
        {
            Usage = new UsageDetails { TotalTokenCount = tokens },
        };
}
