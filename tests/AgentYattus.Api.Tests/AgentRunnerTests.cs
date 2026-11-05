using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Le vrai exécuteur, de bout en bout dans l'API, avec un faux modèle : gratuit et déterministe.</summary>
public sealed class AgentRunnerTests
{
    [Fact]
    public async Task Mission_runs_the_agent_and_audits_each_tool_call()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Un fichier : a.txt."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal("Un fichier : a.txt.", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!.Result);
        Assert.Equal(["Running", "outil:list_files", "Completed"], await api.AuditOfAsync(mission.Id));
        Assert.Equal("Que contient le workspace ?", model.Requests[0].Last().Text);
        Assert.Contains("a.txt", model.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>()
            .Single().Result?.ToString());
    }

    [Fact]
    public async Task Reaching_the_iteration_limit_is_audited()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Réponse forcée."));
        await using var api = new ScriptedModelApiFactory(model, maxIterations: 1);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Explore tout");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal(["Running", "outil:list_files", "limite-de-tours", "Completed"], await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task Mission_fails_without_an_api_key()
    {
        await using var api = new ScriptedModelApiFactory(model: null);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
    }

    [Fact]
    public async Task Mission_fails_without_a_workspace()
    {
        var model = new ScriptedChatClient(Text("jamais appelé"));
        await using var api = new ScriptedModelApiFactory(model, withWorkspace: false);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Empty(model.Requests);
    }
}
