using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentYattus.Api.Tests;

public sealed class MissionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Post_returns_202_and_the_mission_can_be_read()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/missions", new CreateMissionRequest("Relire le README"), Ct);
        var created = await response.Content.ReadFromJsonAsync<MissionDto>(Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/missions/{created!.Id}", response.Headers.Location?.ToString());
        var read = await client.GetFromJsonAsync<MissionDto>($"/missions/{created.Id}", Ct);
        Assert.Equal("Relire le README", read!.Goal);
    }

    [Fact]
    public async Task Post_with_empty_goal_returns_400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/missions", new CreateMissionRequest(" "), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_of_an_unknown_mission_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"/missions/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Runner_timeout_marks_mission_Failed_and_worker_keeps_running()
    {
        var client = factory.CreateClient();

        var failing = await CreateAsync(client, FakeAgentRunner.TimeoutGoal);
        var next = await CreateAsync(client, "Mission suivante");

        Assert.Equal(MissionStatus.Failed, await WaitForFinalStatusAsync(client, failing.Id));
        Assert.Equal(MissionStatus.Completed, await WaitForFinalStatusAsync(client, next.Id));
    }

    [Fact]
    public async Task Mission_reaches_Completed_with_audit_and_hub_events()
    {
        // Le hub diffuse à tous : on filtre sur l'id, d'autres tests de la classe ont pu lancer des missions.
        var events = new ConcurrentQueue<MissionEventDto>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, MissionsHubProtocol.Path),
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();
        hub.On<MissionEventDto>(MissionsHubProtocol.MissionEvent, events.Enqueue);
        await hub.StartAsync(Ct);

        var client = factory.CreateClient();
        var mission = await CreateAsync(client, "Écrire un test");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!events.Any(e => e.MissionId == mission.Id && e.Status == MissionStatus.Completed))
        {
            Assert.True(DateTime.UtcNow < deadline, "La mission n'a pas atteint Completed en 10 s.");
            await Task.Delay(20, Ct);
        }

        Assert.Equal(MissionStatus.Completed, await WaitForFinalStatusAsync(client, mission.Id));
        Assert.Contains("Écrire un test", factory.Runner.Goals);
        Assert.Equal(
            [MissionStatus.Running, MissionStatus.Completed],
            events.Where(e => e.MissionId == mission.Id).Select(e => e.Status));
        using var scope = factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditDb>().Events
            .Where(e => e.MissionId == mission.Id).Select(e => e.Type).ToListAsync(Ct);
        Assert.Equal(["Running", "Completed"], audit);
    }

    private static async Task<MissionDto> CreateAsync(HttpClient client, string goal)
    {
        var response = await client.PostAsJsonAsync("/missions", new CreateMissionRequest(goal), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MissionDto>(Ct))!;
    }

    private static async Task<MissionStatus> WaitForFinalStatusAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = (await client.GetFromJsonAsync<MissionDto>($"/missions/{id}", Ct))!.Status;
            if (status is MissionStatus.Completed or MissionStatus.Failed)
                return status;
            Assert.True(DateTime.UtcNow < deadline, $"Mission {id} bloquée en {status}.");
            await Task.Delay(20, Ct);
        }
    }
}
