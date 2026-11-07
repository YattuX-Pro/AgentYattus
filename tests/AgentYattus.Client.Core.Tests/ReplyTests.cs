using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

/// <summary>Le fil de Rapports et la réponse à l'agent (leçon 118).</summary>
public sealed class ReplyTests
{
    [Fact]
    public async Task The_thread_shows_every_turn_and_a_reply_waits_for_the_agent()
    {
        var mission = new MissionDto(Guid.NewGuid(), "Résume le README", MissionStatus.Completed, DateTimeOffset.UtcNow, "Trois routes.");
        var turns = new List<MissionTurnDto> { new(false, "Trois routes.", DateTimeOffset.UtcNow) };
        string? replied = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                replied = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            object body = path switch
            {
                "/missions" => new[] { mission },
                _ when path.EndsWith("/turns") => turns.ToArray(),
                _ when path.EndsWith("/events") => Array.Empty<AuditEntryDto>(),
                "/projects" => Array.Empty<ProjectDto>(),
                _ => Array.Empty<object>(),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        });
        var vm = new ReportsViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(vm.CanReply);
        vm.ReplyText = "Lesquelles ?";
        await vm.SendReplyCommand.ExecuteAsync(null);

        Assert.Contains("Lesquelles ?", replied);
        Assert.True(vm.IsWaiting);
        Assert.False(vm.CanReply);
        Assert.Equal(("écrit…", "…"), (vm.Thread[^1].What, vm.Thread[^1].Text));
        // Le direct (leçon 120) : la bulle se remplit morceau par morceau.
        vm.ApplyText(new MissionTextDto(mission.Id, "POST "));
        vm.ApplyText(new MissionTextDto(mission.Id, "et deux GET."));
        vm.ApplyText(new MissionTextDto(Guid.NewGuid(), "autre mission"));
        Assert.Equal("POST et deux GET.", vm.Thread[^1].Text);

        // L'agent a répondu : le fil se recharge depuis l'API, la bulle d'attente disparaît.
        turns.Add(new(true, "Lesquelles ?", DateTimeOffset.UtcNow));
        turns.Add(new(false, "POST et deux GET.", DateTimeOffset.UtcNow));
        await vm.ApplyAsync(new MissionEventDto(mission.Id, MissionStatus.Completed, DateTimeOffset.UtcNow));

        Assert.False(vm.IsWaiting);
        Assert.Equal(["Résume le README", "Trois routes.", "Lesquelles ?", "POST et deux GET."], vm.Thread.Select(m => m.Text));
        Assert.Equal([false, false, false, true], vm.Thread.Select(m => m.Last));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
