using AgentYattus.Contracts;
using Microsoft.Extensions.AI;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>La mission de revue à deux agents : l'Analyste lit et rend ses constats, le Rédacteur écrit le rapport.</summary>
public sealed class ReviewTests
{
    [Fact]
    public async Task The_writer_starts_from_the_analyst_findings_and_only_the_writer_can_write()
    {
        var model = new ScriptedChatClient(
            ToolCall("read_file", new() { ["path"] = "a.txt" }, callId: "call_read"),
            Text("haute · a.txt:1 · contenu suspect · le retirer"),
            ToolCall("write_file", new() { ["path"] = "REPORT.md", ["content"] = "# Rapport" }, callId: "call_write"));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Revue de a.txt", MissionKind.Review);
        var paused = await client.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval);

        // L'Analyste ne reçoit que des outils de lecture ; le Rédacteur peut lire et écrire.
        Assert.Equal(["list_files", "read_file"], model.Options[0]!.Tools!.Select(t => t.Name));
        Assert.Equal(["read_file", "write_file"], model.Options[2]!.Tools!.Select(t => t.Name));
        // Les rôles ont chacun leurs instructions.
        Assert.Contains("Analyste", model.Options[0]!.Instructions);
        Assert.Contains("Rédacteur", model.Options[2]!.Instructions);
        // Le passage de relais : la première demande du Rédacteur contient les constats de l'Analyste.
        var handoff = Assert.Single(model.Requests[2]).Text;
        Assert.Contains("Revue de a.txt", handoff);
        Assert.Contains("a.txt:1 · contenu suspect", handoff);
        Assert.Equal("write_file", Assert.Single(paused.PendingApproval!.ToolCalls).Tool);
        Assert.Equal(MissionKind.Review, paused.Kind);
        Assert.Equal(
            ["Running", "agent:analyste", "modele:claude-haiku-5-5", "outil:read_file", "agent:redacteur", "modele:claude-haiku-5-5", "approbation-demandee:write_file", "AwaitingApproval"],
            await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task A_question_only_involves_the_read_only_analyst()
    {
        var model = new ScriptedChatClient(Text("Réponse."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que fait ce projet ?");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.DoesNotContain(model.Options[0]!.Tools!, t => t.Name == "write_file");
        Assert.Single(model.Requests);
    }
}
