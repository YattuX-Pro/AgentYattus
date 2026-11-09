using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using AgentYattus.Api.Modules.Team;
using AgentYattus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.ScriptedChatClient;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class ConversationIdentityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task An_empty_model_answer_is_reported_as_failed_and_cannot_be_validated_as_a_deliverable()
    {
        await using var api = new ScriptedModelApiFactory(new ScriptedChatClient(Text("")));
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Question sans réponse.");
        var done = await client.WaitForStatusAsync(mission.Id, MissionStatus.Failed);
        Assert.Contains("aucune réponse", done.Result);
        var turn = Assert.Single((await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/missions/{mission.Id}/accept-result", new AcceptResultRequest(turn.Id), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_project_group_merges_agents_chronologically_shares_bounded_context_and_respects_project_access()
    {
        var model = new ScriptedChatClient(Text("Constats de l'analyste."), Text("Synthèse du rédacteur."));
        await using var api = new ScriptedModelApiFactory(model);
        var alice = As(api, "alice"); var bob = As(api, "bob");
        var first = await alice.CreateMissionAsync("Analyse le projet.");
        await alice.WaitForStatusAsync(first.Id, MissionStatus.Completed);
        var second = await alice.CreateMissionAsync("Résume ce qui vient d'être dit.", agent: "redacteur");
        await alice.WaitForStatusAsync(second.Id, MissionStatus.Completed);
        var feed = (await alice.GetFromJsonAsync<ProjectGroupMessageDto[]>($"/projects/{first.ProjectId}/group-messages", Ct))!;
        Assert.Equal(4, feed.Length);
        Assert.Equal(new[] { first.Id, first.Id, second.Id, second.Id }, feed.Select(m => m.MissionId));
        Assert.Equal(new[] { true, false, true, false }, feed.Select(m => m.FromUser));
        Assert.Contains("Constats de l'analyste.", string.Join("\n", model.Requests[1].Select(m => m.Text)));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/projects/{first.ProjectId}/group-messages", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_the_current_answer_can_be_validated_and_a_correction_removes_its_validation()
    {
        await using var api = new ScriptedModelApiFactory(new ScriptedChatClient(Text("Version 1."), Text("Version 2.")));
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Analyse ce projet.");
        var first = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.True(first.CanReply);
        var turn = Assert.Single((await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!);
        (await client.PutAsJsonAsync($"/missions/{mission.Id}/accept-result", new AcceptResultRequest(turn.Id), Ct)).EnsureSuccessStatusCode();
        Assert.True((await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", Ct))!.ResultAccepted);
        (await client.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Corrige ce point."), Ct)).EnsureSuccessStatusCode();
        Assert.False((await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed)).ResultAccepted);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/missions/{mission.Id}/accept-result", new AcceptResultRequest(turn.Id), Ct)).StatusCode);
    }

    [Fact]
    public async Task Project_history_can_be_paged_without_crossing_a_projects_access_boundary()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var first = await client.CreateMissionAsync("Première.");
        var second = await client.CreateMissionAsync("Deuxième.");
        var page = (await client.GetFromJsonAsync<MissionDto[]>($"/missions?projectId={first.ProjectId}&before={second.Id}", Ct))!;
        Assert.Contains(page, m => m.Id == first.Id);
        Assert.DoesNotContain(page, m => m.Id == second.Id);
    }

    [Fact]
    public async Task Messages_keep_their_author_after_renaming_the_team_and_a_handoff_keeps_its_recipient()
    {
        var delegation = ToolCall("confier", new() { ["collegue"] = "redacteur", ["consigne"] = "Résume la demande." });
        delegation.Messages[0].Contents.Insert(0, new TextContent("Je confie la synthèse à Malik."));
        var model = new ScriptedChatClient(
            delegation,
            Text("Contribution de Malik."), Text("Le chef répond à nouveau."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var face = await TeamTests.PortraitAsync(client, "ines", "Inès");
        var project = (await client.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!.Single(p => p.IsPersonalDefault).Id;
        await client.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project), new RecruitRequest("analyste", face.Id, "Inès", [], Leader: true), Ct);
        await client.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project), new RecruitRequest("redacteur", face.Id, "Malik", []), Ct);
        var mission = await client.CreateMissionAsync("Demande au rédacteur de résumer.", agent: "analyste");
        var done = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Equal("Inès", done.Interlocutor!.Name);
        var contributions = (await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!;
        Assert.Equal(2, contributions.Length);
        Assert.Equal(("Inès", "Je confie la synthèse à Malik."), (contributions[0].Author!.Name, contributions[0].Text));
        var first = contributions[1];
        Assert.Equal(("Malik", "redacteur"), (first.Author!.Name, first.Author.Role));
        Assert.NotNull(first.Author.ColleagueId);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeamDb>();
            (await db.Colleagues.FindAsync([first.Author.ColleagueId!.Value], Ct))!.Name = "Nouveau nom";
            await db.SaveChangesAsync(Ct);
        }
        (await client.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Merci, chef, et la suite ?"), Ct)).EnsureSuccessStatusCode();
        await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        var turns = (await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!;
        Assert.Equal("Malik", turns[1].Author!.Name);
        Assert.Equal("Inès", turns[^1].Author!.Name);
        Assert.Contains("Tu t'appelles Inès", model.Options[2]!.Instructions);
        Assert.True(turns[0].Id > 0);
    }

    [Fact]
    public async Task Unread_replies_survive_a_reload_and_are_private_and_idempotently_marked_read()
    {
        await using var api = new ScriptedModelApiFactory(new ScriptedChatClient(Text("Réponse privée.")));
        var alice = As(api, "alice"); var bob = As(api, "bob");
        var mission = await alice.CreateMissionAsync("Question privée.");
        await alice.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        var notice = Assert.Single((await alice.GetFromJsonAsync<ConversationNoticeDto[]>("/conversations/notifications", Ct))!);
        Assert.Equal(mission.Id, notice.MissionId);
        Assert.NotNull(notice.TurnId);
        Assert.Empty((await bob.GetFromJsonAsync<ConversationNoticeDto[]>("/conversations/notifications", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsync($"/conversations/notifications/{notice.Id}/read", null, Ct)).StatusCode);
        Assert.Equal(notice.Id, Assert.Single((await alice.GetFromJsonAsync<ConversationNoticeDto[]>("/conversations/notifications", Ct))!).Id);
        (await alice.PutAsync($"/conversations/notifications/{notice.Id}/read", null, Ct)).EnsureSuccessStatusCode();
        (await alice.PutAsync($"/conversations/notifications/{notice.Id}/read", null, Ct)).EnsureSuccessStatusCode();
        Assert.Empty((await alice.GetFromJsonAsync<ConversationNoticeDto[]>("/conversations/notifications", Ct))!);
    }

    [Fact]
    public async Task Drafts_are_saved_before_a_mission_exists_and_cannot_be_read_by_another_user()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice"); var bob = As(api, "bob");
        var project = (await alice.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!.Single(p => p.IsPersonalDefault).Id;
        var route = $"/projects/{project}/conversation-drafts/analyste";
        (await alice.PutAsJsonAsync(route, new ConversationDraftDto("Mon brouillon."), Ct)).EnsureSuccessStatusCode();
        Assert.Equal("Mon brouillon.", (await alice.GetFromJsonAsync<ConversationDraftDto>(route, Ct))!.Text);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(route, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(route, new ConversationDraftDto("Autre."), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_question_is_distinct_from_an_approval_and_stops_the_model_until_the_person_answers()
    {
        var model = new ScriptedChatClient(ToolCall("poser_question", new() { ["question"] = "Quel public visez-vous ?" }), Text("Merci pour la précision."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Prépare une analyse.");
        var first = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.True(first.NeedsReply); Assert.Null(first.PendingApproval);
        Assert.Equal("Quel public visez-vous ?", first.Result);
        Assert.Single(model.Requests);
        (await client.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Des PME."), Ct)).EnsureSuccessStatusCode();
        Assert.False((await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed)).NeedsReply);
    }
}
