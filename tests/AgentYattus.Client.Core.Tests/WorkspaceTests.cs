using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class WorkspaceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };

    [Fact]
    public async Task A_group_keeps_its_shared_draft_and_history_when_changing_addressee_and_does_not_repeat_stream_chunks()
    {
        var team = SceneTests.Team(("analyste", "Inès"), ("redacteur", "Malik"));
        var project = new ProjectDto(team.ProjectId, "Projet", null, null, new("me", "Vous", null), ProjectVisibility.Private,
            false, null, ProjectRole.Owner, true, [], DateTimeOffset.UtcNow);
        var ines = team.Colleagues[0]; var malik = team.Colleagues[1];
        var contact = new MessageAuthorDto("analyste", "Inès", ines.Id, ines.PortraitId);
        var dto = new MissionDto(Guid.NewGuid(), "Analyse ce projet.", MissionStatus.Completed, DateTimeOffset.UtcNow,
            ProjectId: project.Id, Agent: "analyste", Interlocutor: contact, CanReply: true);
        MissionTurnDto[] turns = [new(false, "Constats initiaux.", DateTimeOffset.UtcNow, contact, 1)];
        var drafts = new Dictionary<string, string>();
        var api = new ApiClient(new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("conversation-drafts"))
            {
                if (request.Method == HttpMethod.Put)
                {
                    var body = request.Content!.ReadFromJsonAsync<ConversationDraftDto>(Ct).GetAwaiter().GetResult()!;
                    drafts[path] = body.Text; return new(HttpStatusCode.NoContent);
                }
                return Json(new ConversationDraftDto(drafts.GetValueOrDefault(path, "")));
            }
            if (path.EndsWith("/group-messages")) return Json(new[] { new ProjectGroupMessageDto(dto.Id, 0, true, dto.Goal, dto.CreatedAt, new("user", "Vous")) }
                .Concat(turns.Select(t => new ProjectGroupMessageDto(dto.Id, t.Id, t.FromUser, t.Text, t.At, t.Author))).ToArray());
            if (path.EndsWith("/turns")) return Json(turns);
            if (path.EndsWith("/team")) return Json(team);
            if (path.Contains("/portraits/")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
            if (path == "/projects") return Json(new[] { project });
            if (path == "/missions") return Json(new[] { dto });
            return Json(dto);
        })) { BaseAddress = new Uri("http://api/") });
        var workspace = new WorkspaceViewModel(api, project.Id, project.Name, "Vous");
        await workspace.LoadAsync(ct: Ct);
        Assert.Equal("Inès", workspace.Selected!.Name);
        workspace.Input = "Mon brouillon.";
        await workspace.OpenContactCommand.ExecuteAsync(workspace.Contacts.Single(c => c.Role == "redacteur"));
        Assert.Equal("Mon brouillon.", workspace.Input);
        Assert.Equal("Inès", workspace.Messages[^1].Name);
        workspace.Input = "Brouillon du groupe.";
        await workspace.OpenContactCommand.ExecuteAsync(workspace.Contacts.Single(c => c.Role == "analyste"));
        Assert.Equal("Brouillon du groupe.", workspace.Input);
        Assert.Equal("Inès", workspace.Messages[^1].Name);
        dto = dto with { Status = MissionStatus.Running, CanReply = false };
        await workspace.ApplyAsync(new(dto.Id, MissionStatus.Running, DateTimeOffset.UtcNow));
        Assert.True(workspace.HasWorkingAgents);
        var message = Guid.NewGuid(); var execution = Guid.NewGuid();
        workspace.ApplyActivity(new(dto.Id, execution, new("redacteur", "Malik"), "Prépare sa réponse"));
        Assert.Contains("Malik travaille", workspace.WorkingLabel);
        var chunk = new MissionTextDto(dto.Id, "Contribution ", new("redacteur", "Malik"), message, 1, execution);
        workspace.ApplyText(chunk); workspace.ApplyText(chunk);
        workspace.ApplyText(chunk with { Text = "utile.", Sequence = 2 });
        Assert.Contains("Malik écrit", workspace.WorkingLabel);
        Assert.Equal("Contribution utile.", workspace.Messages[^1].Text);
        Assert.Equal("Malik", workspace.Messages[^1].Name);
        Assert.Equal("Dans le groupe · réponse attendue de Inès", workspace.Recipient);
        turns = [turns[0], new(false, "Contribution utile.", DateTimeOffset.UtcNow, new("redacteur", "Malik"), 2)];
        dto = dto with { Status = MissionStatus.Completed, CanReply = true };
        await workspace.ApplyAsync(new(dto.Id, MissionStatus.Completed, DateTimeOffset.UtcNow));
        Assert.False(workspace.HasWorkingAgents);
        Assert.Equal(new[] { "Vous", "Inès", "Malik" }, workspace.Messages.Select(m => m.Name));
        var notice = new ConversationNoticeDto(Guid.NewGuid(), dto.Id, project.Id, "Projet", MissionStatus.Completed, contact, "Prête", DateTimeOffset.UtcNow, 2);
        Assert.False(workspace.HasRead(notice));
        workspace.Presented(); Assert.True(workspace.HasRead(notice));
        workspace.IsReadingLatest = false; Assert.False(workspace.HasRead(notice));
        workspace.IsReadingLatest = true; workspace.Tab = "Activité"; Assert.False(workspace.HasRead(notice));
        workspace.Tab = "Conversation"; Assert.False(workspace.HasRead(notice with { TurnId = 9 }));
        Assert.False(workspace.HasRead(notice with { ProjectId = Guid.NewGuid() }));
        var oldBubble = workspace.Messages[1];
        await workspace.LoadAsync(ct: Ct);
        Assert.Same(oldBubble, workspace.Messages[1]);
        Assert.Equal(dto.Id, workspace.ActiveMissionId);
    }

    [Fact]
    public async Task Dismissing_the_avatar_cloud_does_not_mark_the_reply_as_read_and_refresh_does_not_repeat_it()
    {
        var notice = new ConversationNoticeDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Projet", MissionStatus.Completed,
            new("analyste", "Inès"), "Ma réponse est prête.", DateTimeOffset.UtcNow, 12);
        var reads = 0; var isRead = false;
        var api = new ApiClient(new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Put) { reads++; return new(HttpStatusCode.NoContent); }
            return Json(new[] { notice });
        })) { BaseAddress = new Uri("http://api/") });
        var inbox = new ConversationInbox(api) { IsReadOnScreen = _ => isRead };
        await inbox.RefreshAsync(Ct);
        Assert.Equal("Inès", inbox.Toast!.Name); Assert.Equal(1, inbox.Count);
        inbox.DismissToastCommand.Execute(null);
        await inbox.RefreshAsync(Ct);
        Assert.Null(inbox.Toast); Assert.Equal(0, reads); Assert.True(inbox.HasUnread);
        isRead = true;
        await inbox.RefreshAsync(Ct);
        Assert.Equal(1, reads); Assert.False(inbox.HasUnread);
        inbox.Clear();
    }

}
