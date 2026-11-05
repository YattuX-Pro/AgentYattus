using System.Text.Json;
using Microsoft.Extensions.AI;
using static AgentYattus.Spikes.Tests.ScriptedChatClient;

namespace AgentYattus.Spikes.Tests;

/// <summary>Les scénarios d'<see cref="AgentLoopTests"/>, rejoués avec la boucle d'Agent Framework.</summary>
public sealed class FrameworkAgentTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-").FullName;
    private readonly WorkspaceTools _tools;

    public FrameworkAgentTests()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        _tools = new WorkspaceTools(_workspace);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Microsoft.Agents.AI.AIAgent CreateAgent(ScriptedChatClient client, int maxIterations = 10) =>
        FrameworkAgent.Create(client, _tools.AsAIFunctions(), maxIterations);

    [Fact]
    public async Task Returns_the_final_text_when_the_model_answers_directly()
    {
        var client = new ScriptedChatClient(Text("Bonjour"));

        var response = await CreateAgent(client).RunAsync("Dis bonjour", cancellationToken: Ct);

        Assert.Equal("Bonjour", response.Text);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Starts_with_the_goal_and_offers_the_tools_to_the_model()
    {
        var client = new ScriptedChatClient(Text("ok"));

        await CreateAgent(client).RunAsync("Analyse le workspace", cancellationToken: Ct);

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

        var response = await CreateAgent(client).RunAsync("Que contient le workspace ?", cancellationToken: Ct);

        Assert.Equal("Il y a un fichier.", response.Text);
        Assert.Equal(2, client.Requests.Count);
        var (message, toolResult) = FindToolResult(client.Requests[1], "call_42");
        Assert.Equal(ChatRole.Tool, message.Role);
        Assert.Contains("a.txt", ResultText(toolResult));
    }

    [Fact]
    public async Task Returns_the_tool_calls_and_results_with_the_response()
    {
        var client = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("fini"));

        var response = await CreateAgent(client).RunAsync("Liste", cancellationToken: Ct);

        var contents = response.Messages.SelectMany(m => m.Contents).ToList();
        Assert.Contains(contents, c => c is FunctionCallContent { Name: "list_files" });
        Assert.Contains(contents, c => c is FunctionResultContent { CallId: "call_1" });
    }

    [Fact]
    public async Task Reports_an_unknown_tool_to_the_model_instead_of_crashing()
    {
        var client = new ScriptedChatClient(ToolCall("delete_everything"), Text("Désolé."));

        var response = await CreateAgent(client).RunAsync("Fais le ménage", cancellationToken: Ct);

        Assert.Equal("Désolé.", response.Text);
        Assert.Contains("delete_everything", ResultText(FindToolResult(client.Requests[1], "call_1").Result));
    }

    [Fact]
    public async Task Reports_a_refused_tool_call_to_the_model_instead_of_crashing()
    {
        var client = new ScriptedChatClient(
            ToolCall("read_file", new() { ["path"] = "../../etc/passwd" }),
            Text("Je ne peux pas lire ce fichier."));

        var response = await CreateAgent(client).RunAsync("Lis /etc/passwd", cancellationToken: Ct);

        Assert.Equal("Je ne peux pas lire ce fichier.", response.Text);
        Assert.Contains("hors du workspace", ResultText(FindToolResult(client.Requests[1], "call_1").Result));
    }

    [Fact]
    public async Task Forces_a_text_answer_when_the_iteration_limit_is_reached()
    {
        ChatResponse[] script =
        [
            .. Enumerable.Range(0, 3).Select(i => ToolCall("list_files", new() { ["path"] = "." }, callId: $"call_{i}")),
            Text("Résumé de ce que j'ai vu."),
        ];
        var client = new ScriptedChatClient(script);

        var response = await CreateAgent(client, maxIterations: 3).RunAsync("Boucle", cancellationToken: Ct);

        // Différence avec notre boucle, qui s'arrête avec le statut MaxIterationsReached :
        // après 3 tours d'outils, le framework rappelle le modèle une dernière fois sans lui proposer
        // d'outils. Le modèle est obligé de conclure par du texte.
        Assert.Equal(4, client.Requests.Count);
        Assert.All(client.Options.Take(3), options => Assert.NotEmpty(options!.Tools!));
        Assert.True(client.Options[3]?.Tools is null or { Count: 0 });
        Assert.Equal("Résumé de ce que j'ai vu.", response.Text);
    }

    [Fact]
    public async Task Sums_the_tokens_reported_by_the_model()
    {
        var client = new ScriptedChatClient(
            ToolCall("list_files", new() { ["path"] = "." }, tokens: 30),
            Text("fini", tokens: 12));

        var response = await CreateAgent(client).RunAsync("Liste", cancellationToken: Ct);

        Assert.Equal(42, response.Usage?.TotalTokenCount);
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
            () => CreateAgent(client).RunAsync("Lis a.txt", cancellationToken: cancellation.Token));
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
