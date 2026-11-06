using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class MissionControlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/missions/{id}/cancel", null, Ct);

    [Fact]
    public async Task A_running_mission_stops_and_one_waiting_its_turn_never_starts()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var running = await client.CreateMissionAsync(FakeAgentRunner.LongGoal + " A");
        var waiting = await client.CreateMissionAsync(FakeAgentRunner.LongGoal + " B");
        await client.WaitForStatusAsync(running.Id, MissionStatus.Running);

        // Une seule place : B attend. On l'arrête avant son départ, puis on arrête A en plein travail.
        Assert.Equal(HttpStatusCode.Accepted, (await CancelAsync(client, waiting.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await CancelAsync(client, running.Id)).StatusCode);

        var stopped = await client.WaitForStatusAsync(running.Id, MissionStatus.Cancelled);
        Assert.Equal("Arrêtée à la demande d'un humain.", stopped.Result);
        Assert.Equal(MissionStatus.Cancelled, (await client.GetFromJsonAsync<MissionDto>($"/missions/{waiting.Id}", Ct))!.Status);
        await Task.Delay(200, Ct);
        Assert.DoesNotContain(FakeAgentRunner.LongGoal + " B", api.Runner.Goals);
        Assert.Contains("arret-demande", await api.AuditOfAsync(running.Id));
        // Déjà arrêtée : rien à arrêter.
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(client, running.Id)).StatusCode);
    }

    [Fact]
    public async Task Only_editors_stop_a_mission_and_finished_missions_cannot_be_stopped()
    {
        await using var api = new ApiFactory();
        var (alice, bob) = (As(api, "alice"), As(api, "bob"));
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var done = await alice.CreateMissionAsync("Vite faite");
        await alice.WaitForFinalStatusAsync(done.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(bob, done.Id)).StatusCode);
        await alice.PutAsJsonAsync(ProjectsProtocol.Members(done.ProjectId!.Value, "bob"), new SetProjectMemberRequest(ProjectRole.Reader), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(bob, done.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(alice, done.Id)).StatusCode);
    }
}
