using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

/// <summary>L'open space : les répliques se jouent lettre par lettre, à la vitesse réglée dans l'admin.</summary>
public sealed class OfficeViewModelTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateTimeOffset T = DateTimeOffset.UtcNow;

    private static OfficeViewModel Create(MissionStatus status, AuditEntryDto[] events, ApprovalDto? approval = null)
    {
        var team = SceneTests.Team(("analyste", "Inès"), ("redacteur", "Malik")) with { TypingMs = 10, PauseMs = 100 };
        var mission = new MissionDto(Guid.NewGuid(), "Revue de Orders", status, T, null, approval, MissionKind.Review, Project);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/portraits/"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            object body = path.EndsWith("/team") ? team : path.EndsWith("/events") ? events : mission;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        });
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") });
        return new OfficeViewModel(api, new MissionItem(mission), "Yatt");
    }

    [Fact]
    public async Task Past_lines_go_straight_to_the_feed_and_the_last_one_types_itself()
    {
        var office = Create(MissionStatus.Running,
            [new("Running", T), new("agent:analyste", T), new("action:analyste:read_file:src/Api.cs", T)]);

        await office.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Léon", "Léon"], office.Feed.Select(l => l.Name));
        Assert.Equal("Inès", office.Analyst.Name);

        // 10 ms par lettre : au bout de 50 ms, cinq lettres sont écrites, et Inès parle.
        office.Tick(TimeSpan.FromMilliseconds(50));
        Assert.Equal("Je li", office.Analyst.Bubble);
        Assert.True(office.Analyst.IsSpeaking);
        Assert.Equal("Inès", office.Typing);
        Assert.Equal(SceneObject.Folder, office.Carrying);

        // Toute la phrase, puis le temps de lecture : la réplique entre dans le fil et la bulle se ferme.
        office.Tick(TimeSpan.FromMilliseconds(500));
        office.Tick(TimeSpan.FromMilliseconds(200));
        Assert.Equal("Je lis src/Api.cs.", office.Feed[^1].Text);
        Assert.False(office.Analyst.IsSpeaking);
        Assert.Equal("lit le code", office.Analyst.Status);
        Assert.False(office.IsCarrying);
    }

    [Fact]
    public async Task A_pending_write_knocks_on_the_door_and_approval_still_needs_the_full_content()
    {
        var content = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"ligne {i}"));
        var approval = new ApprovalDto(Guid.NewGuid(), [new ToolCallDto("write_file", $$"""{"path":"REPORT.md","content":"{{content.Replace("\n", "\\n")}}"}""")]);
        var office = Create(MissionStatus.AwaitingApproval,
            [new("Running", T), new("agent:redacteur", T), new("approbation-demandee:write_file", T), new("AwaitingApproval", T)], approval);

        await office.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(office.IsKnocking);
        // La règle de la leçon 26 reste : pas d'approbation sans avoir affiché tout le contenu.
        Assert.False(office.Mission.CanApprove);
        office.Mission.ShowAllCommand.Execute(null);
        Assert.True(office.Mission.CanApprove);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
