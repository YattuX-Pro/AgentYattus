using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class AgentsAndReportsTests
{
    private static ApiClient Api(Func<string, object> respond) => new(new HttpClient(new StubHandler(request =>
    {
        var body = respond(request.RequestUri!.AbsolutePath);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
    })) { BaseAddress = new Uri("http://api/") });

    [Fact]
    public async Task Agents_screen_selects_the_first_agent()
    {
        AgentDto[] agents = [new("Analyste", "Explore.", "claude-haiku-5-5", 10, [new("write_file", "Écrit.", true)])];
        var vm = new AgentsViewModel(Api(_ => agents));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        // Sans collègue à ce poste ni droit sur les chiffres : le nom de l'agent, et « — ».
        Assert.Equal("Analyste", vm.Selected!.Name);
        Assert.True(vm.Selected.IsSelected);
        Assert.True(Assert.Single(vm.Selected.Agent.Tools).RequiresApproval);
        Assert.Equal("—", vm.Selected.RateLabel);
    }

    [Fact]
    public async Task Reports_keep_completed_missions_and_count_what_was_executed()
    {
        var done = new MissionDto(Guid.NewGuid(), "Rapport", MissionStatus.Completed, DateTimeOffset.UtcNow, "Fini.");
        MissionDto[] missions =
        [
            done,
            new(Guid.NewGuid(), "En cours", MissionStatus.Running, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Échouée", MissionStatus.Failed, DateTimeOffset.UtcNow),
        ];
        AuditEntryDto[] events =
        [
            new("outil:list_files", DateTimeOffset.UtcNow), new("outil:read_file", DateTimeOffset.UtcNow),
            new("decision:refusee", DateTimeOffset.UtcNow), new("decision:approuvee", DateTimeOffset.UtcNow),
        ];
        var vm = new ReportsViewModel(Api(path => path.EndsWith("/events") ? events : missions));

        await vm.LoadAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 50 && vm.Stats is null; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(done.Id, Assert.Single(vm.Reports).Id);
        Assert.Equal(new ReportStats(Reads: 2, Approved: 1, Refused: 1), vm.Stats);
        // Le rapport raconté : votre question, puis la réponse de l'agent.
        Assert.Collection(vm.Thread,
            you => Assert.Equal(("Vous", "Rapport", true), (you.Name, you.Text, you.IsYou)),
            agent => Assert.Equal(("Analyste", "Fini.", false, (string?)null), (agent.Name, agent.Text, agent.IsYou, agent.File)));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
