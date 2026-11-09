using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class RecruitmentTests
{
    [Fact]
    public async Task Hub_chunks_update_the_same_bubble_before_completion_even_when_the_hub_precedes_the_http_acknowledgement()
    {
        var id = Guid.NewGuid();
        var enteredPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ShellViewModel? shell = null;
        using var http = new HttpClient(new AsyncHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                shell!.ApplyText(new MissionTextDto(Guid.NewGuid(), "Autre mission : ne pas afficher."));
                shell.ApplyText(new MissionTextDto(id, "Bon"));
                return new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new MissionDto(id, "Bonjour", MissionStatus.Pending,
                    DateTimeOffset.UtcNow, Kind: MissionKind.Recruitment)) };
            }
            enteredPoll.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new MissionDto(id, "Bonjour", MissionStatus.Completed,
                DateTimeOffset.UtcNow, "Bonjour !", Kind: MissionKind.Recruitment)) };
        })) { BaseAddress = new Uri("http://api/") };
        var api = new ApiClient(http);
        var recruit = new RecruitViewModel(api, Guid.NewGuid());
        shell = new ShellViewModel(api, new HomeViewModel(api)) { Current = recruit };
        var vm = recruit.Rh;
        vm.Input = "Bonjour";
        var send = vm.SendCommand.ExecuteAsync(null);
        await enteredPoll.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(vm.IsBusy);
        Assert.False(send.IsCompleted);
        var bubble = vm.Messages[^1];
        Assert.False(bubble.FromUser);
        Assert.Equal("Bon", bubble.Text);
        var changed = false;
        bubble.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(RecruitmentMessage.Text);
        shell.ApplyText(new MissionTextDto(id, "jour"));
        Assert.True(changed);
        Assert.Equal("Bonjour", bubble.Text);
        release.SetResult();
        await send;
        Assert.Same(bubble, vm.Messages[^1]);
        Assert.Equal("Bonjour !", bubble.Text);
        Assert.Equal(2, vm.Messages.Count);
        shell.ApplyText(new MissionTextDto(id, "texte tardif"));
        Assert.Equal("Bonjour !", bubble.Text);
    }

    [Fact]
    public async Task An_interview_can_be_reopened_from_its_persisted_mission_without_calling_the_model()
    {
        var id = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath.EndsWith("/turns"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] {
                    new MissionTurnDto(false, "Pour quels clients ?", DateTimeOffset.UtcNow),
                    new MissionTurnDto(true, "Des PME.", DateTimeOffset.UtcNow),
                    new MissionTurnDto(false, "Quel livrable attendez-vous ?", DateTimeOffset.UtcNow) }) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new MissionDto(id, "Un commercial.", MissionStatus.Completed,
                DateTimeOffset.UtcNow, "Quel livrable attendez-vous ?", Kind: MissionKind.Recruitment)) };
        })) { BaseAddress = new Uri("http://api/") };
        var vm = new RecruitmentViewModel(new ApiClient(http), Guid.NewGuid());
        await vm.RestoreAsync(id, TestContext.Current.CancellationToken);
        Assert.True(vm.IsOpen);
        Assert.Equal(4, vm.Messages.Count);
        Assert.Equal("Un commercial.", vm.Messages[0].Text);
        Assert.Equal("Quel livrable attendez-vous ?", vm.Messages[^1].Text);
        vm.Input = "Un document Word.";
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_dialogue_keeps_the_same_mission_for_questions_and_receives_the_profile()
    {
        var project = Guid.NewGuid();
        var id = Guid.NewGuid();
        var posts = new List<string>();
        var replied = false;
        var draft = new RecruitmentDraftDto("Commercial", "Prépare des propositions commerciales.", "Une consigne complète pour préparer et vérifier les propositions.",
            ["read_file"], [new("Proposition", "Pour toute proposition commerciale.", "Vérifie les besoins, les faits fournis et les conditions à confirmer.")]);
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                posts.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (path.EndsWith("/messages")) replied = true;
                return new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new MissionDto(id, "Un commercial.", MissionStatus.Pending, DateTimeOffset.UtcNow, Kind: MissionKind.Recruitment, ProjectId: project)) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new MissionDto(id, "Un commercial.", MissionStatus.Completed, DateTimeOffset.UtcNow,
                replied ? "Voici la fiche." : "Pour quels clients ?", Kind: MissionKind.Recruitment, ProjectId: project, RecruitmentDraft: replied ? draft : null)) };
        })) { BaseAddress = new Uri("http://api/") };
        var vm = new RecruitmentViewModel(new ApiClient(http), project) { Input = "Un commercial." };
        await vm.SendCommand.ExecuteAsync(null);
        Assert.False(vm.HasDraft);
        Assert.Contains(vm.Messages, m => !m.FromUser && m.Text == "Pour quels clients ?");
        vm.Input = "Des PME ; des propositions Word.";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.HasDraft);
        Assert.Equal("Commercial", vm.Draft!.Name);
        Assert.Equal(4, vm.Messages.Count);
        var created = System.Text.Json.JsonSerializer.Deserialize<CreateMissionRequest>(posts[0], System.Text.Json.JsonSerializerOptions.Web)!;
        Assert.Equal(MissionKind.Recruitment, created.Kind);
        Assert.Equal(project, created.ProjectId);
        Assert.Equal(2, posts.Count);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
