using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class MissionOwnershipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mission_belongs_to_its_project_and_strangers_see_nothing()
    {
        await using var api = new ApiFactory();
        var (alice, bob) = (As(api, "alice"), As(api, "bob"));
        await bob.GetAsync(IdentityProtocol.Me, Ct);

        var mission = await alice.CreateMissionAsync("Revue du module Llm");
        await alice.WaitForFinalStatusAsync(mission.Id);

        var mine = Assert.Single((await alice.GetFromJsonAsync<MissionDto[]>("/missions", Ct))!);
        Assert.Equal(("Personnel", "alice"), (mine.ProjectName, mine.CreatedBy));
        Assert.Empty((await bob.GetFromJsonAsync<MissionDto[]>("/missions", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/missions/{mission.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/missions/{mission.Id}/events", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, Guid.NewGuid()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.PostAsJsonAsync("/missions", new CreateMissionRequest("Intrusion", ProjectId: mine.ProjectId), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_reader_sees_but_does_not_act_and_an_editor_launches_missions()
    {
        await using var api = new ApiFactory();
        var (alice, bob) = (As(api, "alice"), As(api, "bob"));
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var mission = await alice.CreateMissionAsync("Revue");
        await alice.WaitForFinalStatusAsync(mission.Id);
        var projectId = mission.ProjectId!.Value;

        await alice.PutAsJsonAsync(ProjectsProtocol.Members(projectId, "bob"), new SetProjectMemberRequest(ProjectRole.Reader), Ct);
        Assert.Single((await bob.GetFromJsonAsync<MissionDto[]>("/missions", Ct))!);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/missions/{mission.Id}/events", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, Guid.NewGuid()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bob.PostAsJsonAsync("/missions", new CreateMissionRequest("Par bob", ProjectId: projectId), Ct)).StatusCode);

        await alice.PutAsJsonAsync(ProjectsProtocol.Members(projectId, "bob"), new SetProjectMemberRequest(ProjectRole.Editor), Ct);
        var byBob = (await (await bob.PostAsJsonAsync("/missions", new CreateMissionRequest("Par bob, éditeur", ProjectId: projectId), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        Assert.Equal((projectId, "bob"), (byBob.ProjectId!.Value, byBob.CreatedBy));
    }

    [Fact]
    public async Task Missions_run_in_the_project_workspace_and_old_missions_are_for_instance_admins_only()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");
        var root = As(api, "root", instanceAdmin: true);
        var project = (await (await alice.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Agent Yattus"), Ct))
            .Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        var folder = Directory.CreateTempSubdirectory().FullName;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(folder), Ct);

        var response = await alice.PostAsJsonAsync("/missions", new CreateMissionRequest("Dans le dossier du projet", ProjectId: project.Id), Ct);
        var mission = (await response.Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await alice.WaitForFinalStatusAsync(mission.Id);
        Assert.Equal(folder, api.Runner.Workspaces["Dans le dossier du projet"]);

        // Sans dossier choisi, le dossier commun du serveur ne sert qu'aux projets d'un administrateur de l'instance.
        var stray = await alice.CreateMissionAsync("Lire le dossier commun");
        Assert.Equal(MissionStatus.Failed, await alice.WaitForFinalStatusAsync(stray.Id));
        Assert.Contains("dossier de travail", (await alice.GetFromJsonAsync<MissionDto>($"/missions/{stray.Id}", Ct))!.Result);
        Assert.False(api.Runner.Workspaces.ContainsKey("Lire le dossier commun"));
        var byAdmin = await root.CreateMissionAsync("Dans le dossier configuré");
        Assert.Equal(MissionStatus.Completed, await root.WaitForFinalStatusAsync(byAdmin.Id));
        Assert.Null(api.Runner.Workspaces["Dans le dossier configuré"]);

        // Une mission d'avant les projets (sans ProjectId) : seuls les administrateurs de l'instance la voient.
        var old = Guid.CreateVersion7();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MissionsDb>();
            db.Missions.Add(new Mission { Id = old, Goal = "Ancienne", Status = MissionStatus.Completed, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/missions/{old}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await root.GetAsync($"/missions/{old}", Ct)).StatusCode);
        Assert.Contains((await root.GetFromJsonAsync<MissionDto[]>("/missions", Ct))!, m => m.Id == old);
    }

    [Fact]
    public async Task The_live_feed_only_reaches_people_who_can_read_the_mission()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");
        await As(api, "bob").GetAsync(IdentityProtocol.Me, Ct);
        await alice.GetAsync(IdentityProtocol.Me, Ct);

        async Task<(HubConnection Hub, ConcurrentQueue<MissionEventDto> Events)> ListenAs(string user)
        {
            var events = new ConcurrentQueue<MissionEventDto>();
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(api.Server.BaseAddress, MissionsHubProtocol.Path), options =>
                {
                    options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                    options.Headers[TestAuthHandler.UserHeader] = user;
                    options.Headers[TestAuthHandler.RolesHeader] = "membre";
                })
                .Build();
            hub.On<MissionEventDto>(MissionsHubProtocol.MissionEvent, events.Enqueue);
            await hub.StartAsync(Ct);
            return (hub, events);
        }

        var (aliceHub, aliceEvents) = await ListenAs("alice");
        var (bobHub, bobEvents) = await ListenAs("bob");
        await using (aliceHub)
        await using (bobHub)
        {
            var mission = await alice.CreateMissionAsync("En direct");
            await alice.WaitForFinalStatusAsync(mission.Id);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!aliceEvents.Any(e => e.MissionId == mission.Id && e.Status is MissionStatus.Completed or MissionStatus.Failed))
            {
                Assert.True(DateTime.UtcNow < deadline, "alice n'a pas reçu l'événement.");
                await Task.Delay(20, Ct);
            }
            await Task.Delay(100, Ct);
            Assert.Empty(bobEvents);
        }
    }
}
