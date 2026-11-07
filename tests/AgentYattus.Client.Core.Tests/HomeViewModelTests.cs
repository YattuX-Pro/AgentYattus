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
    public async Task Projects_where_one_can_launch_are_offered_and_the_chosen_one_is_sent()
    {
        var owner = new UserRefDto("u", "u", null);
        ProjectDto Project(string name, ProjectRole access, bool personal = false) => new(
            Guid.NewGuid(), name, null, null, owner, ProjectVisibility.Private, personal, null, access, false, [], DateTimeOffset.UtcNow);
        var personal = Project("Personnel", ProjectRole.Owner, personal: true);
        var shared = Project("API facturation", ProjectRole.Editor);
        var readOnly = Project("Lecture seule", ProjectRole.Reader);
        string? body = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "But", MissionStatus.Pending, DateTimeOffset.UtcNow)),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = path == "/projects" ? JsonContent.Create(new[] { readOnly, shared, personal }) : JsonContent.Create(Array.Empty<MissionDto>()),
            };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["API facturation", "Personnel"], vm.Projects.Select(p => p.Name));
        Assert.Equal(personal.Id, vm.SelectedProject!.Id);
        vm.SelectedProject = shared;
        vm.Goal = "But";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        Assert.Contains($"\"projectId\":\"{shared.Id}\"", body);
    }

    [Fact]
    public async Task A_question_is_launched_with_the_chosen_agent_and_a_review_keeps_its_circuit()
    {
        var bodies = new List<string>();
        AgentDto[] agents = [new("Analyste", "", "m", 10, [], "analyste"), new("Testeur", "", "m", 10, [], "testeur", Builtin: false)];
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "But", MissionStatus.Pending, DateTimeOffset.UtcNow)),
                };
            }
            var path = request.RequestUri!.AbsolutePath;
            object content = path == "/agents" ? agents : path == "/projects" ? Array.Empty<ProjectDto>() : Array.Empty<MissionDto>();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("analyste", vm.SelectedAgent!.Key);
        vm.SelectedAgent = vm.Agents[1];
        vm.Goal = "Vérifie les tests";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        vm.WriteReport = true;
        vm.Goal = "Revue";
        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.Contains("\"agent\":\"testeur\"", bodies[0]);
        Assert.Contains("\"agent\":null", bodies[1]);
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
        var sent = new List<string>();
        var handler = new StubHandler(request =>
        {
            sent.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath == "/projects" ? JsonContent.Create(Array.Empty<ProjectDto>()) : JsonContent.Create(missions),
            };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["http://api/missions?limit=50", "http://api/projects", "http://api/agents"], sent);
        Assert.Equal(missions.Select(m => m.Id), vm.Missions.Select(m => m.Id));
        Assert.True(vm.Missions[0].IsAwaitingApproval);
        Assert.Equal("À valider", vm.Missions[0].StatusLabel);
    }

    [Fact]
    public async Task A_suggestion_launches_a_two_agent_review()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "Audit de sécurité", MissionStatus.Pending,
                    DateTimeOffset.UtcNow, Kind: MissionKind.Review)),
            };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        vm.UseSuggestionCommand.Execute("Audit de sécurité");
        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.True(vm.WriteReport);
        Assert.Contains("\"kind\":\"Review\"", body);
        Assert.True(vm.Missions[0].IsReview);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
