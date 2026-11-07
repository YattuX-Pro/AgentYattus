using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Ajouter des agents depuis l'admin (ADR 0013), et les choisir au lancement d'une mission.</summary>
public sealed class CustomAgentsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CreateAgentRequest Tester = new(
        "Testeur de régression", "Vérifie que les tests couvrent le code modifié.",
        "Tu es le Testeur d'Agent Yattus. Tu lis les tests et tu dis ce qui manque, fichier par fichier.", ["read_file", "write_file"]);

    [Fact]
    public async Task Admins_add_edit_and_retire_agents_with_tools_from_the_existing_list()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);

        var created = await root.PostAsJsonAsync(AgentsProtocol.Path, Tester, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var agent = (await created.Content.ReadFromJsonAsync<AgentDto>(Ct))!;
        Assert.Equal(("testeur-de-regression", false), (agent.Key, agent.Builtin));
        // L'écriture garde l'accord humain, et les garde-fous le rappellent : ce n'est pas un réglage de l'agent.
        Assert.True(agent.Tools.Single(t => t.Name == "write_file").RequiresApproval);
        Assert.Contains("Chaque écriture attend la décision d'un humain.", agent.Guardrails);

        // Même nom : un autre identifiant, jamais un écrasement.
        var twin = (await (await root.PostAsJsonAsync(AgentsProtocol.Path, Tester, Ct)).Content.ReadFromJsonAsync<AgentDto>(Ct))!;
        Assert.Equal("testeur-de-regression-2", twin.Key);

        var bad = await root.PostAsJsonAsync(AgentsProtocol.Path, Tester with { Name = "x", Tools = ["run_shell"], Instructions = "court" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = await bad.Content.ReadAsStringAsync(Ct);
        Assert.Contains("name", problem);
        Assert.Contains("tools", problem);
        Assert.Contains("instructions", problem);

        await root.PutAsJsonAsync(AgentsProtocol.Profile(agent.Key), new UpdateAgentProfileRequest("Testeur", "Relit les tests.", ["read_file"]), Ct);
        var listed = (await root.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!;
        Assert.Equal(["analyste", "redacteur", "testeur-de-regression", "testeur-de-regression-2"], listed.Select(a => a.Key));
        Assert.Equal(("Testeur", "read_file"), (listed[2].Name, Assert.Single(listed[2].Tools).Name));

        Assert.Equal(HttpStatusCode.NoContent, (await root.DeleteAsync(AgentsProtocol.Role(agent.Key), Ct)).StatusCode);
        Assert.DoesNotContain((await root.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!, a => a.Key == agent.Key);
        // L'Analyste et le Rédacteur ne se retirent pas, et un membre ne gère pas les agents.
        Assert.Equal(HttpStatusCode.NotFound, (await root.DeleteAsync(AgentsProtocol.Role("analyste"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(api, "alice").PostAsJsonAsync(AgentsProtocol.Path, Tester, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_question_runs_with_the_chosen_agent_and_retry_keeps_it()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var key = (await (await root.PostAsJsonAsync(AgentsProtocol.Path, Tester, Ct)).Content.ReadFromJsonAsync<AgentDto>(Ct))!.Key;

        var response = await root.PostAsJsonAsync("/missions", new CreateMissionRequest(FakeAgentRunner.TimeoutGoal, Agent: key), Ct);
        var mission = (await response.Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        Assert.Equal(key, mission.Agent);
        await root.WaitForStatusAsync(mission.Id, MissionStatus.Failed);
        Assert.Equal(key, api.Runner.Agents[mission.Id]);

        var retry = (await (await root.PostAsync($"/missions/{mission.Id}/retry", null, Ct)).Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        Assert.Equal(key, retry.Agent);

        // Pas d'agent pour une Revue (circuit Analyste puis Rédacteur), ni d'agent inconnu ou retiré.
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("Revue", MissionKind.Review, Agent: key), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("But", Agent: "inconnu"), Ct)).StatusCode);
        await root.DeleteAsync(AgentsProtocol.Role(key), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("But", Agent: key), Ct)).StatusCode);
        // La relance ne contourne pas ce contrôle : un agent retiré ne repart pas.
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsync($"/missions/{mission.Id}/retry", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_model_receives_the_agent_instructions_and_only_its_tools_and_writing_still_waits_for_a_human()
    {
        var model = new ScriptedChatClient(ToolCall("write_file", new() { ["path"] = "TESTS.md", ["content"] = "# Manques" }, callId: "w"));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var key = (await (await client.PostAsJsonAsync(AgentsProtocol.Path, Tester with { Tools = ["write_file"] }, Ct)).Content.ReadFromJsonAsync<AgentDto>(Ct))!.Key;

        var created = await client.PostAsJsonAsync("/missions", new CreateMissionRequest("Liste les tests manquants", Agent: key), Ct);
        var mission = (await created.Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        var paused = await client.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval);

        Assert.Equal(["write_file"], model.Options[0]!.Tools!.Select(t => t.Name));
        Assert.StartsWith("Tu es le Testeur d'Agent Yattus.", model.Options[0]!.Instructions);
        Assert.EndsWith("Chaque écriture attend la décision d'un humain.", model.Options[0]!.Instructions);
        Assert.Equal("write_file", Assert.Single(paused.PendingApproval!.ToolCalls).Tool);
    }
}
