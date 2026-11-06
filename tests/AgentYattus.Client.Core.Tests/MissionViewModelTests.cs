using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class MissionViewModelTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTimeOffset T = DateTimeOffset.UtcNow;

    private static (MissionViewModel Vm, Func<int> Requests) Create(MissionDto mission, AuditEntryDto[] events)
    {
        var count = 0;
        var handler = new StubHandler(request =>
        {
            count++;
            object body = request.RequestUri!.AbsolutePath.EndsWith("/events") ? events : mission;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        });
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") });
        return (new MissionViewModel(api, new MissionItem(mission with { Status = MissionStatus.Pending })), () => count);
    }

    [Fact]
    public async Task Load_counts_the_tools_used_and_reads_the_journal()
    {
        var (vm, _) = Create(
            new MissionDto(Id, "Analyse", MissionStatus.Completed, T, "Trois fichiers."),
            [new("Running", T), new("outil:list_files", T), new("outil:read_file", T), new("outil:read_file", T), new("Completed", T)]);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MissionStatus.Completed, vm.Mission.Status);
        Assert.Equal("Trois fichiers.", vm.Result);
        Assert.Equal([new ToolUsage("list_files", 1), new ToolUsage("read_file", 2)], vm.Tools);
        Assert.Equal(5, vm.Journal.Count);
        Assert.Equal("Terminée", vm.LastEntry!.Label);
        Assert.False(vm.HumanInvolved);
    }

    [Fact]
    public async Task Load_shows_the_human_decision()
    {
        var (vm, _) = Create(
            new MissionDto(Id, "Rapport", MissionStatus.Completed, T, "Écrit."),
            [new("Running", T), new("approbation-demandee:write_file", T), new("AwaitingApproval", T),
             new("decision:approuvee", T), new("Running", T), new("outil:write_file", T), new("Completed", T)]);

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsApproved);
        Assert.True(vm.HumanInvolved);
        Assert.Contains(vm.Journal, e => e.IsDecision && e.Label == "Écriture approuvée");
    }

    [Fact]
    public async Task Hub_events_reload_only_our_mission()
    {
        var (vm, requests) = Create(new MissionDto(Id, "Analyse", MissionStatus.Running, T), []);

        await vm.ApplyAsync(new MissionEventDto(Guid.NewGuid(), MissionStatus.Completed, T));
        Assert.Equal(0, requests());

        await vm.ApplyAsync(new MissionEventDto(Id, MissionStatus.Running, T));
        Assert.Equal(2, requests());
    }

    [Fact]
    public async Task Opening_a_mission_from_home_shows_its_screen()
    {
        var (vm, _) = Create(new MissionDto(Id, "Analyse", MissionStatus.Running, T), []);
        var api = new ApiClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AuditEntryDto>()) }))
        {
            BaseAddress = new Uri("http://api/"),
        });
        var home = new HomeViewModel(api);
        var shell = new ShellViewModel(api, home);

        home.OpenMissionCommand.Execute(vm.Mission);

        var screen = Assert.IsType<MissionViewModel>(shell.Current);
        Assert.Same(vm.Mission, screen.Mission);
        Assert.True(shell.IsMissionsSection);
        shell.GoHomeCommand.Execute(null);
        Assert.Same(home, shell.Current);

        // La gestion se fait dans l'espace admin web : le bureau demande seulement à l'ouvrir.
        Uri? opened = null;
        var withAdmin = new ShellViewModel(api, home) { AdminUrl = new Uri("http://admin.local/") };
        withAdmin.OpenUrlRequested += uri => opened = uri;
        withAdmin.OpenAdminCommand.Execute(null);
        Assert.Equal(new Uri("http://admin.local/"), opened);
    }

    [Fact]
    public async Task Approving_posts_the_decision_and_shows_exactly_what_will_be_written()
    {
        var content = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"ligne {i}"));
        var approvalId = Guid.NewGuid();
        var mission = new MissionDto(Id, "Rapport", MissionStatus.AwaitingApproval, T, null, new ApprovalDto(approvalId,
            [new ToolCallDto("write_file", System.Text.Json.JsonSerializer.Serialize(new { path = "docs/REPORT.md", content }))]));
        HttpRequestMessage? decision = null;
        string? decisionBody = null;
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                decision = request;
                decisionBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            object body = request.RequestUri!.AbsolutePath.EndsWith("/events") ? Array.Empty<AuditEntryDto>() : mission;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        });
        var vm = new MissionViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }), new MissionItem(mission));

        await vm.LoadAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single(vm.PendingApproval);
        Assert.Equal("Écrire REPORT.md ?", item.Title);
        Assert.Equal("docs/REPORT.md", item.Target);
        Assert.Equal(6, item.Preview.Count);
        Assert.Equal(4, item.MoreLines);
        // Tant que les 4 dernières lignes n'ont pas été affichées, on ne peut pas approuver.
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.True(vm.NeedsFullReview);
        Assert.True(vm.RefuseCommand.CanExecute(null));

        vm.ShowAllCommand.Execute(null);
        Assert.True(vm.ApproveCommand.CanExecute(null));
        await vm.ApproveCommand.ExecuteAsync(null);

        Assert.Equal($"http://api/missions/{Id}/decision", decision!.RequestUri!.ToString());
        Assert.Contains("\"approved\":true", decisionBody);
        Assert.Contains($"\"approvalId\":\"{approvalId}\"", decisionBody);
    }

    [Fact]
    public async Task A_decision_already_taken_elsewhere_is_explained()
    {
        var mission = new MissionDto(Id, "Rapport", MissionStatus.AwaitingApproval, T, null,
            new ApprovalDto(Guid.NewGuid(), [new ToolCallDto("write_file", "{\"path\":\"REPORT.md\",\"content\":\"x\"}")]));
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("/events")
                    ? JsonContent.Create(Array.Empty<AuditEntryDto>())
                    : JsonContent.Create(mission),
            });
        var vm = new MissionViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }), new MissionItem(mission));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        await vm.RefuseCommand.ExecuteAsync(null);

        Assert.Contains("plus d'actualité", vm.Error);
    }

    [Fact]
    public void Hidden_characters_and_sensitive_locations_are_made_visible()
    {
        var item = ApprovalItem.From(new ToolCallDto("write_file",
            System.Text.Json.JsonSerializer.Serialize(new { path = ".git/hooks/pre-commit", content = "echo ok\r\nsafe\u202Etxt.exe" })));

        Assert.NotNull(item.Warning);
        Assert.Equal("echo ok␍", item.Preview[0]);
        Assert.Equal("safe⟪U+202E⟫txt.exe", item.Preview[1]);

        // Séparateur de ligne Unicode, espace insécable, caractère invisible du bloc des étiquettes (hors plan de base).
        var tricky = ApprovalItem.From(new ToolCallDto("write_file",
            System.Text.Json.JsonSerializer.Serialize(new { path = "a.txt", content = "a\u2028b\u00A0c\U000E0041d" })));
        Assert.Equal("a⟪U+2028⟫b⟪U+00A0⟫c⟪U+E0041⟫d", tricky.Preview[0]);

        // Un caractère « vide » dans le nom du fichier est aussi marqué dans le titre.
        var blank = ApprovalItem.From(new ToolCallDto("write_file",
            System.Text.Json.JsonSerializer.Serialize(new { path = "rapport\u3164.md", content = "x" })));
        Assert.Equal("Écrire rapport⟪U+3164⟫.md ?", blank.Title);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
