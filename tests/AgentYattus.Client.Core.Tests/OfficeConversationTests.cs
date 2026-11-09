using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class OfficeConversationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Conversation_streams_then_reloads_saved_turns_without_duplicating_the_answer()
    {
        // Une question à Inès, l'Analyste recrutée comme chef d'équipe (leçon 129) : elle répond depuis le bureau du chef.
        var dto = new MissionDto(Guid.NewGuid(), "Bonjour", MissionStatus.Running, DateTimeOffset.UtcNow, ProjectId: Guid.NewGuid(), Agent: "analyste");
        var team = SceneTests.Team(("analyste", "Inès"), ("redacteur", "Malik"));
        MissionTurnDto[] turns = [];
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/portraits/"))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            object body = path.EndsWith("/turns") ? turns : path.EndsWith("/events") ? Array.Empty<AuditEntryDto>() : path.EndsWith("/team") ? team : dto;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) };
        });
        var office = new OfficeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }), new MissionItem(dto), "Vous");
        await office.LoadAsync(Ct);
        Assert.Equal(("Inès", "Siège libre"), (office.Leader.Name, office.Analyst.Name));
        Assert.Equal("Bonjour", Assert.Single(office.Conversation).Text);
        Assert.Same(office.Leader, office.Author);
        Assert.False(office.CanReply);
        office.ApplyText(new(dto.Id, "Bon"));
        office.ApplyText(new(Guid.NewGuid(), "ignoré"));
        office.ApplyText(new(dto.Id, "jour !"));
        Assert.Equal("Bonjour !", office.LiveText);
        var recruitmentId = Guid.NewGuid();
        dto = dto with { Status = MissionStatus.Completed, Result = "Bonjour !", RecruitmentMissionId = recruitmentId };
        turns = [new(false, "Bonjour !", DateTimeOffset.UtcNow, new MessageAuthorDto("analyste", "Inès"))];
        await office.ApplyAsync(new(dto.Id, MissionStatus.Completed, DateTimeOffset.UtcNow));
        Assert.Equal(new[] { "Bonjour", "Bonjour !" }, office.Conversation.Select(m => m.Text));
        Assert.Equal("Inès", office.Conversation[1].Name);
        Assert.True(office.Conversation[1].IsLeader);
        Guid? opened = null;
        office.RecruitmentOpened += id => opened = id;
        Assert.Equal(recruitmentId, office.Mission.RecruitmentMissionId);
        office.OpenRecruitmentCommand.Execute(null);
        Assert.Equal(recruitmentId, opened);
        Assert.Null(office.LiveText);
        Assert.True(office.CanReply);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reply_continues_the_same_mission_and_preserves_the_draft_on_failure(bool fail)
    {
        var dto = new MissionDto(Guid.NewGuid(), "Bonjour", MissionStatus.Completed, DateTimeOffset.UtcNow, "Bonjour !", Agent: "analyste");
        string? path = null;
        var handler = new StubHandler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            return new(fail ? HttpStatusCode.BadRequest : HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new { errors = new { text = new[] { "Message refusé." } } }),
            };
        });
        var office = new OfficeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }), new MissionItem(dto), "Vous") { ReplyText = "Tu peux préciser ?" };
        await office.SendReplyCommand.ExecuteAsync(null);
        Assert.Equal($"/missions/{dto.Id}/messages", path);
        if (fail)
        {
            Assert.Equal("Tu peux préciser ?", office.ReplyText);
            Assert.Equal("Message refusé.", office.Error);
            Assert.True(office.CanReply);
            Assert.Single(office.Conversation);
        }
        else
        {
            Assert.Equal("", office.ReplyText);
            Assert.Equal("Tu peux préciser ?", office.Conversation[^1].Text);
            Assert.Equal(MissionStatus.Pending, office.Mission.Mission.Status);
            Assert.False(office.CanReply);
            Assert.True(office.IsResponding);
        }
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

}
