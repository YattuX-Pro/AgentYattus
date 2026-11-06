using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Le vrai exécuteur, de bout en bout dans l'API, avec un faux modèle : gratuit et déterministe.</summary>
public sealed class AgentRunnerTests
{
    [Fact]
    public async Task A_mission_keeps_its_tokens_and_its_cost_at_the_catalog_price()
    {
        // Claude Haiku 5.5 au catalogue : 0,10 $ et 0,50 $ par million de tokens (entrée, sortie).
        var model = new ScriptedChatClient(Text("Réponse.", input: 1_000, output: 200));
        await using var api = new ScriptedModelApiFactory(model, modelName: "anthropic/claude-haiku-5-5");
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Combien ça coûte ?");
        await client.WaitForFinalStatusAsync(mission.Id);

        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!;
        // 1 000 × 0,10 + 200 × 0,50 = 200 micro-dollars.
        Assert.Equal((1_000L, 200L, 0.0002m), (done.InputTokens, done.OutputTokens, done.Cost));
        Assert.Contains("usage:analyste:1000:200", await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task Mission_runs_the_agent_and_audits_each_tool_call()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Un fichier : a.txt."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal("Un fichier : a.txt.", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!.Result);
        Assert.Equal(["Running", "modele:claude-haiku-5-5", "outil:list_files", "usage:analyste:0:0", "Completed"], await api.AuditOfAsync(mission.Id));
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
        Assert.Equal(["Running", "modele:claude-haiku-5-5", "outil:list_files", "limite-de-tours", "usage:analyste:0:0", "Completed"], await api.AuditOfAsync(mission.Id));
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
