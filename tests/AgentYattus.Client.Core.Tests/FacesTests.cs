using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

// Les visages de l'équipe partout où l'on parle d'un agent : un cache par client, des prénoms dans les phrases.
public sealed class FacesTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Leon = Guid.NewGuid(), Ines = Guid.NewGuid(), Malik = Guid.NewGuid(), You = Guid.NewGuid();

    private static ProjectTeamDto Team() => new(Project,
        [
            new(Guid.NewGuid(), Project, "analyste", Ines, "Inès", [], null, DateTimeOffset.UtcNow, true, null),
            new(Guid.NewGuid(), Project, "redacteur", Malik, "Malik", [], null, DateTimeOffset.UtcNow, true, null),
        ],
        "Léon", Leon, You, 38, 1800, new Dictionary<string, string>(), true);

    private static (ApiClient Api, List<string> Calls) Api(AgentDto[]? agents = null)
    {
        var calls = new List<string>();
        var api = new ApiClient(new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add(path);
            if (path.StartsWith("/team/portraits/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            object? body = path switch
            {
                _ when path.EndsWith("/team", StringComparison.Ordinal) => Team(),
                "/agents" => agents ?? [],
                "/missions" => Array.Empty<MissionDto>(),
                "/projects" => Array.Empty<ProjectDto>(),
                "/team/portraits" => new PortraitDto[] { new(Malik, "malik", "Malik", true, true, "", ""), new(Ines, "ines", "Inès", true, true, "", "") },
                "/team/traits" => new TraitDto[] { new("calme", "Calme", "") },
                _ => null,
            };
            return body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        })) { BaseAddress = new Uri("http://api/") });
        return (api, calls);
    }

    [Fact]
    public async Task A_team_is_fetched_once_and_an_empty_seat_has_no_face()
    {
        var (api, calls) = Api();

        var ines = await api.Faces.AgentAsync(Project, "analyste");
        var again = await api.Faces.AgentAsync(Project, "analyste");
        var nobody = await api.Faces.AgentAsync(Project, "securite");

        Assert.Equal("Inès", ines!.Name);
        Assert.NotNull(ines.Work);
        Assert.Same(ines.Work, again!.Work);
        Assert.Null(nobody);
        Assert.Single(calls, c => c.EndsWith("/team", StringComparison.Ordinal));
        Assert.Equal("Vous", (await api.Faces.ManagerAsync(Project))!.Name);
    }

    [Fact]
    public async Task A_review_shows_both_faces_and_the_writer_knocks_at_your_door()
    {
        var (api, _) = Api();
        var mission = new MissionItem(new MissionDto(Guid.NewGuid(), "Revue", MissionStatus.AwaitingApproval, DateTimeOffset.UtcNow,
            Kind: MissionKind.Review, ProjectId: Project));

        await mission.LoadFacesAsync(api.Faces);

        Assert.Equal(["Inès", "Malik"], mission.Faces.Select(f => f.Name));
        Assert.Equal("Malik frappe à votre porte", mission.Line);
        mission.Status = MissionStatus.Completed;
        Assert.Equal("Terminée · Inès et Malik", mission.Line);
    }

    [Fact]
    public async Task An_agent_without_a_colleague_keeps_a_readable_name()
    {
        var (api, _) = Api();
        var mission = new MissionItem(new MissionDto(Guid.NewGuid(), "Audit", MissionStatus.Running, DateTimeOffset.UtcNow,
            ProjectId: Project, Agent: "analyste-securite"));

        await mission.LoadFacesAsync(api.Faces);

        var face = Assert.Single(mission.Faces);
        Assert.Equal("Analyste securite", face.Name);
        Assert.Null(face.Work);
        Assert.Equal("Analyste securite y travaille", mission.Line);
    }

    [Fact]
    public async Task Clicking_a_face_on_home_picks_who_does_the_mission()
    {
        AgentDto[] agents = [new("Analyste", "Explore.", "m", 10, [], "analyste"), new("Rédacteur", "Écrit.", "m", 10, [], "redacteur")];
        var (api, _) = Api(agents);
        api.Faces.DefaultProject = Project;
        var vm = new HomeViewModel(api);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["chef", "analyste", "redacteur"], vm.Crew.Select(m => m.Key));
        Assert.Equal("Quelle mission pour Inès ?", vm.Title);
        Assert.Equal("Inès", Assert.Single(vm.Chosen).Name);

        // Le chef d'équipe ne prend pas de mission seul ; Malik, si.
        vm.PickCommand.Execute(vm.Crew[0]);
        Assert.Equal("analyste", vm.SelectedAgent!.Key);
        vm.PickCommand.Execute(vm.Crew[2]);
        Assert.Equal("redacteur", vm.SelectedAgent!.Key);
        Assert.True(vm.Crew[2].IsSelected);
        Assert.False(vm.Crew[1].IsSelected);

        // Une revue : Inès puis Malik, toujours.
        vm.WriteReport = true;
        Assert.Equal("Inès analyse, Malik rédige.", vm.Title);
        Assert.Equal(["Inès", "Malik"], vm.Chosen.Select(m => m.Name));
        Assert.Equal("Vous", vm.Me!.Name);
    }

    [Fact]
    public void An_agent_card_says_its_success_rate_and_cost()
    {
        var card = new AgentCard(new AgentDto("Analyste", "Explore.", "m", 10, [], "analyste"))
        {
            Stats = new AgentPerformanceDto("analyste", 4, 3, 0.0025m),
        };

        Assert.Equal(0.75, card.Rate);
        Assert.Equal("75 %", card.RateLabel);
        Assert.Equal(4, card.Missions);
        Assert.Contains("0", card.Cost);
        Assert.EndsWith("$", card.Cost);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task Changing_a_face_reopens_the_recruit_screen_on_that_post_with_the_colleague_prefilled()
    {
        AgentDto[] agents = [new("Analyste", "Explore.", "m", 10, [], "analyste"), new("Rédacteur", "Écrit.", "m", 10, [], "redacteur")];
        var (api, _) = Api(agents);
        var screen = new AgentsViewModel(api, Project);
        (Guid Project, AgentCard? Card)? asked = null;
        screen.CustomizeRequested += (project, card) => asked = (project, card);

        await screen.LoadAsync(TestContext.Current.CancellationToken);
        screen.CustomizeCommand.Execute(screen.Agents[1]);

        Assert.True(screen.CanCustomize);
        Assert.IsType<RecruitTile>(screen.Tiles[^1]);
        Assert.Equal(Project, asked!.Value.Project);
        Assert.Equal("Malik", asked.Value.Card!.Colleague!.Name);

        var recruit = new RecruitViewModel(api, Project, "redacteur", asked.Value.Card.Colleague);
        await recruit.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("redacteur", recruit.Role!.Key);
        Assert.Equal(Malik, recruit.Selected!.Portrait.Id);
        Assert.Equal("Malik", recruit.Name);
        Assert.Equal("Nouveau visage pour Malik", recruit.Title);
        Assert.Equal("Enregistrer ce visage", recruit.Action);
    }
}
