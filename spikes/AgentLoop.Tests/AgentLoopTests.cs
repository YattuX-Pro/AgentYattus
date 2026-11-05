using System.Text.Json;
using Microsoft.Extensions.AI;
using static AgentYattus.Spikes.Tests.ScriptedChatClient;

namespace AgentYattus.Spikes.Tests;

public sealed class AgentLoopTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-").FullName;
    private readonly WorkspaceTools _tools;

    public AgentLoopTests()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        _tools = new WorkspaceTools(_workspace);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AgentLoop CreateLoop(ScriptedChatClient client, int maxIterations = 10) =>
        new(client, _tools.AsAIFunctions(), maxIterations);

    [Fact]
    public async Task Returns_the_final_text_when_the_model_answers_directly()
    {
        var client = new ScriptedChatClient(Text("Bonjour"));

        var result = await CreateLoop(client).RunAsync("Dis bonjour", Ct);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("Bonjour", result.FinalText);
        Assert.Equal(1, result.Iterations);
    }

    [Fact]
    public async Task Starts_with_the_goal_and_offers_the_tools_to_the_model()
    {
        var client = new ScriptedChatClient(Text("ok"));

        await CreateLoop(client).RunAsync("Analyse le workspace", Ct);

        var firstMessage = Assert.Single(client.Requests[0]);
        Assert.Equal(ChatRole.User, firstMessage.Role);
        Assert.Equal("Analyse le workspace", firstMessage.Text);
        Assert.Equal(new[] { "list_files", "read_file" }, client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Executes_the_requested_tool_and_sends_the_result_back()
    {
        var client = new ScriptedChatClient(
            ToolCall("list_files", new() { ["path"] = "." }, callId: "call_42"),
            Text("Il y a un fichier."));

        var result = await CreateLoop(client).RunAsync("Que contient le workspace ?", Ct);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("Il y a un fichier.", result.FinalText);
        Assert.Equal(2, result.Iterations);
        var (message, toolResult) = FindToolResult(client.Requests[1], "call_42");
        Assert.Equal(ChatRole.Tool, message.Role);
        Assert.Contains("a.txt", ResultText(toolResult));
    }

    [Fact]
    public async Task Keeps_the_model_messages_unchanged_in_the_history()
    {
        var toolCall = ToolCall("list_files", new() { ["path"] = "." });
        var client = new ScriptedChatClient(toolCall, Text("fini"));

        await CreateLoop(client).RunAsync("Liste", Ct);

        Assert.Contains(toolCall.Messages[0], client.Requests[1]);
    }

    [Fact]
    public async Task Reports_an_unknown_tool_to_the_model_instead_of_crashing()
    {
        var client = new ScriptedChatClient(ToolCall("delete_everything"), Text("Désolé."));

        var result = await CreateLoop(client).RunAsync("Fais le ménage", Ct);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Contains("delete_everything", ResultText(FindToolResult(client.Requests[1], "call_1").Result));
    }

    [Fact]
    public async Task Reports_a_refused_tool_call_to_the_model_instead_of_crashing()
    {
        var client = new ScriptedChatClient(
            ToolCall("read_file", new() { ["path"] = "../../etc/passwd" }),
            Text("Je ne peux pas lire ce fichier."));

        var result = await CreateLoop(client).RunAsync("Lis /etc/passwd", Ct);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Contains("hors du workspace", ResultText(FindToolResult(client.Requests[1], "call_1").Result));
    }

    [Fact]
    public async Task Stops_when_the_iteration_limit_is_reached()
    {
        var endlessToolCalls = Enumerable.Range(0, 5)
            .Select(i => ToolCall("list_files", new() { ["path"] = "." }, callId: $"call_{i}"))
            .ToArray();
        var client = new ScriptedChatClient(endlessToolCalls);

        var result = await CreateLoop(client, maxIterations: 3).RunAsync("Boucle", Ct);

        Assert.Equal(AgentRunStatus.MaxIterationsReached, result.Status);
        Assert.Equal(3, result.Iterations);
        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public async Task Sums_the_tokens_reported_by_the_model()
    {
        var client = new ScriptedChatClient(
            ToolCall("list_files", new() { ["path"] = "." }, tokens: 30),
            Text("fini", tokens: 12));

        var result = await CreateLoop(client).RunAsync("Liste", Ct);

        Assert.Equal(42, result.TotalTokens);
    }

    [Fact]
    public async Task Lets_cancellation_propagate_while_a_tool_runs()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var client = new ScriptedChatClient(
            ToolCall("read_file", new() { ["path"] = "a.txt" }),
            Text("jamais atteint"))
        {
            OnRequest = _ => cancellation.Cancel(),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateLoop(client).RunAsync("Lis a.txt", cancellation.Token));
        Assert.Single(client.Requests);
    }

    private static (ChatMessage Message, FunctionResultContent Result) FindToolResult(
        IEnumerable<ChatMessage> history, string callId)
    {
        foreach (var message in history)
        {
            foreach (var result in message.Contents.OfType<FunctionResultContent>())
            {
                if (result.CallId == callId)
                {
                    return (message, result);
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Aucun FunctionResultContent avec le CallId '{callId}' dans l'historique envoyé au modèle.");
    }

    private static string ResultText(FunctionResultContent result) => result.Result switch
    {
        null => "",
        string text => text,
        var other => JsonSerializer.Serialize(other),
    };
}
