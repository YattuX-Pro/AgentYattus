using AgentYattus.Api.Modules.Agents;
using Microsoft.Extensions.AI;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class TokenEconomyTests
{
    [Fact]
    public async Task A_mission_stops_between_calls_at_its_budget_and_keeps_the_usage_already_billed()
    {
        var call = ScriptedChatClient.ToolCall("list_files", new() { ["path"] = "." });
        call.Usage = new UsageDetails { InputTokenCount = 1_000, OutputTokenCount = 200, TotalTokenCount = 1_200 };
        var model = new ScriptedChatClient(call, ScriptedChatClient.Text("Ne doit pas partir."));
        await using var api = new ScriptedModelApiFactory(model, modelName: "anthropic/claude-haiku-5-5");
        api.Settings["Agents:TokenEconomy:MaxMissionTokens"] = "1000";
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Explore.");
        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!;
        Assert.Contains("budget de tokens", done.Result);
        Assert.Single(model.Requests);
        Assert.Equal((1_000L, 200L, 0.0002m), (done.InputTokens, done.OutputTokens, done.Cost));
    }

    [Fact]
    public async Task A_follow_up_cannot_reset_the_mission_token_budget()
    {
        var model = new ScriptedChatClient(ScriptedChatClient.Text("Première réponse.", input: 1_000, output: 200),
            ScriptedChatClient.Text("Ne doit pas partir."));
        await using var api = new ScriptedModelApiFactory(model);
        api.Settings["Agents:TokenEconomy:MaxMissionTokens"] = "1300";
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Réponds.");
        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        (await client.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Continue."), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Single(model.Requests);
        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!;
        Assert.Equal((1_000L, 200L), (done.InputTokens, done.OutputTokens));
    }

    [Fact]
    public async Task Large_old_results_are_shortened_at_the_provider_boundary_without_changing_the_saved_history()
    {
        var original = new string('x', 20_000);
        var history = new List<ChatMessage> { new(ChatRole.User, "Conserve ma demande exacte.") };
        for (var i = 0; i < 6; i++)
        {
            history.Add(new(ChatRole.Assistant, [new FunctionCallContent($"c{i}", "read_file", new Dictionary<string, object?> { ["path"] = $"{i}.txt" })]));
            history.Add(new(ChatRole.Tool, [new FunctionResultContent($"c{i}", original)]));
        }
        var model = new ScriptedChatClient(ScriptedChatClient.Text("Fait."));
        var options = new ChatOptions { MaxOutputTokens = 16_000, Instructions = "Règles fixes." };
        var bounded = new TokenEconomyChatClient(model, new TokenEconomyOptions());
        await foreach (var _ in bounded.GetStreamingResponseAsync(history, options, TestContext.Current.CancellationToken)) { }

        var sent = model.Requests.Single();
        Assert.Equal(history.Count, sent.Count);
        Assert.Equal(history[0].Text, sent[0].Text);
        var results = sent.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Equal(6, results.Count);
        Assert.Equal("c0", results[0].CallId);
        Assert.True(results[0].Result!.ToString()!.Length < 1_000);
        Assert.True(results[^1].Result!.ToString()!.Length < 12_200);
        Assert.All(history.SelectMany(m => m.Contents).OfType<FunctionResultContent>(), r => Assert.Equal(original, r.Result));
        Assert.Equal(4_096, model.Options.Single()!.MaxOutputTokens);
        Assert.Equal(16_000, options.MaxOutputTokens);
    }

    [Fact]
    public async Task Oversized_user_context_stops_before_any_paid_call_and_is_never_silently_cut()
    {
        var model = new ScriptedChatClient(ScriptedChatClient.Text("Ne doit pas partir."));
        var bounded = new TokenEconomyChatClient(model, new TokenEconomyOptions());
        var error = await Assert.ThrowsAsync<AgentBudgetExceededException>(() => bounded.GetResponseAsync(
            [new ChatMessage(ChatRole.User, new string('a', 125_000))], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("limite de contexte", error.Message);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void A_shortened_command_result_keeps_its_status_and_its_final_error()
    {
        var bounded = new TokenEconomyChatClient(new ScriptedChatClient(), new TokenEconomyOptions());
        var sent = bounded.Prepare([
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("command", "run_command")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("command", "Code de sortie 1\n" + new string('x', 20_000) + "\nERREUR_FINALE")]),
        ], null).Messages;
        var text = Assert.IsType<FunctionResultContent>(sent[1].Contents.Single()).Result!.ToString()!;
        Assert.StartsWith("Code de sortie 1", text);
        Assert.EndsWith("ERREUR_FINALE", text);
        Assert.Contains("contenu intermédiaire omis", text);
    }

    [Fact]
    public void Loaded_skill_procedures_and_approval_requests_are_preserved()
    {
        var procedure = new string('p', 15_000);
        var call = new FunctionCallContent("skill", "use_skill", new Dictionary<string, object?> { ["name"] = "conventions" });
        var request = new ToolApprovalRequestContent("approval", new FunctionCallContent("write", "write_file", new Dictionary<string, object?> { ["path"] = "a.txt", ["content"] = "exact" }));
        var history = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [call]),
            new(ChatRole.Tool, [new FunctionResultContent("skill", procedure)]),
            new(ChatRole.Assistant, [request]),
        };
        var bounded = new TokenEconomyChatClient(new ScriptedChatClient(), new TokenEconomyOptions());
        var sent = bounded.Prepare(history, null).Messages;
        Assert.Equal(procedure, Assert.IsType<FunctionResultContent>(sent[1].Contents.Single()).Result);
        Assert.Same(request, sent[2].Contents.Single());
    }
}
