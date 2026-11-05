using AgentYattus.Contracts;
using Microsoft.Extensions.AI;

namespace AgentYattus.Evals;

/// <summary>Le harnais d'évaluation lui-même, vérifié gratuitement avec un faux modèle.</summary>
public sealed class HarnessTests
{
    [Fact]
    public async Task A_review_is_approved_graded_on_its_report_and_priced_from_the_traces()
    {
        // L'Analyste répond, le Rédacteur demande à écrire REPORT.md, puis conclut après l'approbation du harnais.
        var model = new Scripted(
            Answer("haute · OrderService.cs:8 · .Result bloquant · passer en await\nmoyenne · OrderRepository.cs:9 · N+1 · Include\nbasse · OrderEndpoints.cs:7 · CancellationToken ignoré · le propager", 4_000, 300),
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call_w", "write_file", new Dictionary<string, object?>
                {
                    ["path"] = "REPORT.md",
                    ["content"] = "# Revue\n\n- .Result bloquant (OrderService.cs:8)\n- N+1 (OrderRepository.cs:9)",
                })]))
            {
                Usage = new UsageDetails { InputTokenCount = 5_000, OutputTokenCount = 200 },
            },
            Answer("REPORT.md écrit.", 5_300, 20));
        var review = new EvalCase("revue", "Revue de performance", MissionKind.Review,
            [".Result", "N+1", "CancellationToken"], [".Result", "OrderRepository.cs"]);

        var (verdict, cost) = await ModelEval.RunAsync("claude-haiku-5-5", model, review);

        Assert.True(verdict.Success, verdict.Reason);
        Assert.Equal(Pricing.Cost("claude-haiku-5-5", 4_000, 300) + Pricing.Cost("claude-haiku-5-5", 5_000, 200)
                     + Pricing.Cost("claude-haiku-5-5", 5_300, 20), cost);
    }

    private static ChatResponse Answer(string text, long input, long output) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output } };

    private sealed class Scripted(params ChatResponse[] script) : IChatClient
    {
        private readonly Queue<ChatResponse> _script = new(script);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_script.Dequeue());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
