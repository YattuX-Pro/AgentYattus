using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public void Launch_is_disabled_while_the_goal_is_blank()
    {
        var vm = new HomeViewModel(new ApiClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException()))));

        vm.Goal = "   ";

        Assert.False(vm.LaunchMissionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Launch_posts_the_goal_and_shows_the_created_mission()
    {
        var mission = new MissionDto(Guid.NewGuid(), "Relire le README", MissionStatus.Pending, DateTimeOffset.UtcNow);
        HttpRequestMessage? sent = null;
        var handler = new StubHandler(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(mission) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }))
        {
            Goal = "Relire le README",
        };

        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("http://api/missions", sent.RequestUri!.ToString());
        Assert.Equal(mission.Id, Assert.Single(vm.Missions).Id);
        Assert.Equal("", vm.Goal);

        vm.Apply(new MissionEventDto(mission.Id, MissionStatus.Completed, DateTimeOffset.UtcNow));

        Assert.True(vm.Missions[0].IsCompleted);
    }

    [Fact]
    public async Task Launch_shows_an_error_when_the_api_fails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }))
        {
            Goal = "Relire le README",
        };

        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Missions);
    }

    [Fact]
    public async Task Load_shows_the_missions_returned_by_the_api()
    {
        MissionDto[] missions =
        [
            new(Guid.NewGuid(), "Écrire le rapport", MissionStatus.AwaitingApproval, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Relire le README", MissionStatus.Completed, DateTimeOffset.UtcNow, "Tout va bien."),
        ];
        HttpRequestMessage? sent = null;
        var handler = new StubHandler(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(missions) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http://api/missions?limit=50", sent!.RequestUri!.ToString());
        Assert.Equal(missions.Select(m => m.Id), vm.Missions.Select(m => m.Id));
        Assert.True(vm.Missions[0].IsAwaitingApproval);
        Assert.Equal("À valider", vm.Missions[0].StatusLabel);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
