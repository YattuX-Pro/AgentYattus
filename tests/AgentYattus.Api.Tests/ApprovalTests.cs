using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>L'humain dans la boucle : l'agent s'arrête avant d'écrire, et attend une décision.</summary>
public sealed class ApprovalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Microsoft.Extensions.AI.ChatResponse WriteReport() =>
        ToolCall("write_file", new() { ["path"] = "REPORT.md", ["content"] = "# Rapport" }, callId: "call_write");

    [Fact]
    public async Task Approved_write_is_executed_and_the_mission_completes()
    {
        var model = new ScriptedChatClient(WriteReport(), Text("Rapport écrit."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var report = Path.Combine(api.Workspace, "REPORT.md");

        var mission = await client.CreateMissionAsync("Écris le rapport");
        var paused = await client.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval);

        var call = Assert.Single(paused.PendingApproval!.ToolCalls);
        Assert.Equal("write_file", call.Tool);
        Assert.Contains("REPORT.md", call.Arguments);
        Assert.False(File.Exists(report), "Rien ne doit être écrit avant la décision.");

        var decision = await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, paused.PendingApproval.Id), Ct);
        Assert.Equal(HttpStatusCode.Accepted, decision.StatusCode);

        var done = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Equal("# Rapport", await File.ReadAllTextAsync(report, Ct));
        Assert.Equal("Rapport écrit.", done.Result);
        Assert.Null(done.PendingApproval);
        Assert.Equal(
            ["Running", "approbation-demandee:write_file", "AwaitingApproval", "decision:approuvee", "Running", "outil:write_file", "Completed"],
            await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task Refused_write_is_not_executed_and_the_model_is_told()
    {
        var model = new ScriptedChatClient(WriteReport(), Text("D'accord, je n'écris rien."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Écris le rapport");
        var paused = await client.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval);
        await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(false, paused.PendingApproval!.Id), Ct);

        var done = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.False(File.Exists(Path.Combine(api.Workspace, "REPORT.md")));
        Assert.Equal("D'accord, je n'écris rien.", done.Result);
        // Le modèle reçoit un résultat pour son appel refusé : il sait que l'écriture n'a pas eu lieu.
        Assert.Contains(model.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>(),
            result => result.CallId == "call_write");
        Assert.Contains("decision:refusee", await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task A_paused_mission_does_not_block_the_next_one()
    {
        // Le faux modèle répond dans l'ordre des appels : la mission A demande d'écrire,
        // puis la mission B répond, puis A, une fois approuvée, conclut.
        var model = new ScriptedChatClient(WriteReport(), Text("B terminée."), Text("A terminée."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var a = await client.CreateMissionAsync("A : écris le rapport");
        var pausedA = await client.WaitForStatusAsync(a.Id, MissionStatus.AwaitingApproval);
        var b = await client.CreateMissionAsync("B : réponds");

        Assert.Equal("B terminée.", (await client.WaitForStatusAsync(b.Id, MissionStatus.Completed)).Result);
        Assert.Equal(MissionStatus.AwaitingApproval, (await client.GetFromJsonAsync<MissionDto>($"/missions/{a.Id}", Ct))!.Status);

        await client.PostAsJsonAsync($"/missions/{a.Id}/decision", new DecisionRequest(true, pausedA.PendingApproval!.Id), Ct);
        Assert.Equal("A terminée.", (await client.WaitForStatusAsync(a.Id, MissionStatus.Completed)).Result);
    }

    [Fact]
    public async Task A_decision_is_accepted_only_once_and_only_for_the_approval_shown()
    {
        var model = new ScriptedChatClient(WriteReport(), Text("fini"));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var unknown = await client.PostAsJsonAsync($"/missions/{Guid.NewGuid()}/decision", new DecisionRequest(true, Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var mission = await client.CreateMissionAsync("Écris le rapport");
        var paused = await client.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval);
        var approvalId = paused.PendingApproval!.Id;
        var stale = await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, Guid.NewGuid()), Ct);
        var first = await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, approvalId), Ct);
        var second = await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(false, approvalId), Ct);

        // Une décision qui porte sur une autre demande que celle en attente est refusée.
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        var late = await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, approvalId), Ct);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }
}
