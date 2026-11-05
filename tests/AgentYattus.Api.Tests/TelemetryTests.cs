using System.Collections.Concurrent;
using System.Diagnostics;
using AgentYattus.Contracts;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Tests;

public sealed class TelemetryTests
{
    // Valeurs propres à ce test : l'écouteur est global, d'autres tests tournent en parallèle.
    private const int InputTokens = 4242;
    private const int OutputTokens = 1717;

    [Fact]
    public async Task Each_model_call_is_traced_with_its_duration_and_tokens_but_not_its_content()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var model = new ScriptedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "contenu-confidentiel"))
        {
            Usage = new UsageDetails { InputTokenCount = InputTokens, OutputTokenCount = OutputTokens },
        });
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Réponds");
        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));

        var ours = activities.Where(a => Convert.ToInt64(a.GetTagItem("gen_ai.usage.input_tokens") ?? 0L) == InputTokens).ToList();
        var agent = Assert.Single(ours, a => a.DisplayName.StartsWith("invoke_agent analyste", StringComparison.Ordinal));
        var chat = Assert.Single(ours, a => a.DisplayName.StartsWith("chat", StringComparison.Ordinal));
        // L'appel au modèle est rattaché à l'exécution de l'agent, qui récapitule aussi les tokens.
        Assert.Equal(agent.SpanId, chat.ParentSpanId);
        Assert.Equal(OutputTokens, Convert.ToInt64(chat.GetTagItem("gen_ai.usage.output_tokens")));
        Assert.True(chat.Duration > TimeSpan.Zero);
        // Ni le but, ni la réponse ne partent dans la télémétrie.
        Assert.DoesNotContain(activities.SelectMany(a => a.TagObjects), tag => tag.Value?.ToString()?.Contains("contenu-confidentiel") == true);
    }
}
