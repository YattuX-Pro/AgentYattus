using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class RecruitmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static RecruitmentDraftDto Profile => new("Rédacteur commercial", "Prépare des propositions commerciales adaptées au client.",
        "Tu prépares des propositions commerciales claires. Demande le public et le résultat attendu, puis vérifie les faits et les prix fournis.",
        ["read_file", "write_document"],
        [new("Proposition client", "Pour toute proposition commerciale destinée à un client.", "Identifie le besoin et les faits fournis. Présente le bénéfice, le périmètre, les conditions connues et les points à confirmer.")]);

    [Fact]
    public async Task A_profile_can_be_confirmed_once_and_the_confirmation_is_persisted_without_another_model_call()
    {
        var model = new ScriptedChatClient(ToolCall("preparer_recrutement", new() { ["profile"] = Profile }), Text("Le profil est prêt."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var portrait = await TeamTests.PortraitAsync(client, "nora", "Nora");
        var mission = await client.CreateMissionAsync("Prépare un analyste.", MissionKind.Recruitment);
        var ready = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.NotNull(ready.RecruitmentRequestId);
        var request = new RecruitRequest("analyste", portrait.Id, "Nora", [], RecruitmentSource: ready.RecruitmentRequestId, RecruitmentMission: mission.Id);
        var first = await client.PostAsJsonAsync(TeamProtocol.ProjectColleagues(ready.ProjectId!.Value), request, Ct);
        first.EnsureSuccessStatusCode();
        var added = (await first.Content.ReadFromJsonAsync<ColleagueDto>(Ct))!;
        var duplicate = await client.PostAsJsonAsync(TeamProtocol.ProjectColleagues(ready.ProjectId.Value), request, Ct);
        duplicate.EnsureSuccessStatusCode();
        Assert.Equal(added.Id, (await duplicate.Content.ReadFromJsonAsync<ColleagueDto>(Ct))!.Id);
        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", Ct))!;
        Assert.True(done.RecruitmentAdded);
        Assert.Contains("rejoint", done.Result);
        var turns = (await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!;
        Assert.Single(turns, t => t.Text.Contains("recrutement est terminé"));
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task The_hr_asks_questions_then_returns_a_valid_profile_without_creating_anything()
    {
        var model = new ScriptedChatClient(Text("Quels clients visez-vous et quel document attendez-vous ?", input: 100, output: 20),
            ToolCall("preparer_recrutement", new() { ["profile"] = Profile }), Text("Le profil est prêt à relire.", input: 200, output: 30));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Il me faut quelqu'un pour mes propositions commerciales.", MissionKind.Recruitment);
        var first = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Null(first.RecruitmentDraft);
        Assert.Contains("Quels clients", first.Result);
        Assert.Equal(["preparer_recrutement", "poser_question"], model.Options[0]!.Tools!.Select(t => t.Name));
        Assert.Contains("Tu es l'agent RH", model.Options[0]!.Instructions);
        Assert.DoesNotContain(model.Options[0]!.Tools!, t => t.Name is "read_file" or "run_command" or "write_file" or "propose_skill");

        (await client.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Des PME ; un document Word clair et court."), Ct)).EnsureSuccessStatusCode();
        var final = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Equal("Le profil est prêt à relire.", final.Result);
        Assert.Equal(Profile.Name, final.RecruitmentDraft!.Name);
        Assert.Equal(Profile.Skills[0].Body, final.RecruitmentDraft.Skills[0].Body);
        Assert.Equal((300L, 50L), (final.InputTokens, final.OutputTokens));
        Assert.Contains(model.Requests[1], m => m.Text?.Contains("Quels clients") == true);
        Assert.Contains(model.Requests[1], m => m.Text == "Des PME ; un document Word clair et court.");
        Assert.DoesNotContain((await client.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!, a => !a.Builtin);
        Assert.Empty((await client.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!);
        var turns = (await client.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!;
        Assert.Equal("Le profil est prêt à relire.", turns[^1].Text);
        Assert.DoesNotContain("\"draft\"", turns[^1].Text);
    }

    [Fact]
    public async Task An_invented_tool_is_rejected_and_no_draft_is_exposed()
    {
        var model = new ScriptedChatClient(ToolCall("preparer_recrutement", new() { ["profile"] = Profile with { Tools = ["send_email"] } }),
            Text("L'envoi de mail n'est pas disponible ; je peux préparer les messages."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Un agent commercial.", MissionKind.Recruitment);
        var final = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Null(final.RecruitmentDraft);
        Assert.Contains(model.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>(),
            r => r.Result!.ToString()!.Contains("Fiche refusée"));
    }

    [Fact]
    public async Task An_invalid_revision_cannot_leave_an_older_profile_available_for_creation()
    {
        var model = new ScriptedChatClient(
            ToolCall("preparer_recrutement", new() { ["profile"] = Profile }, "valid"),
            ToolCall("preparer_recrutement", new() { ["profile"] = Profile with { Tools = ["send_email"] } }, "invalid"),
            Text("L'envoi n'est pas disponible ; précisons le périmètre."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Un agent commercial.", MissionKind.Recruitment);
        var final = await client.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Null(final.RecruitmentDraft);
    }

    [Fact]
    public async Task Project_readers_and_outsiders_cannot_start_an_hr_interview()
    {
        var model = new ScriptedChatClient(Text("Ne doit pas partir."));
        await using var api = new ScriptedModelApiFactory(model);
        var owner = As(api, "owner");
        var reader = As(api, "reader");
        var outsider = As(api, "outsider");
        await reader.GetAsync(IdentityProtocol.Me, Ct);
        var project = (await (await owner.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Équipe"), Ct)).Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        (await owner.PutAsJsonAsync(ProjectsProtocol.Members(project.Id, "reader"), new SetProjectMemberRequest(ProjectRole.Reader), Ct)).EnsureSuccessStatusCode();
        var request = new CreateMissionRequest("Je veux recruter un agent.", MissionKind.Recruitment, project.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/missions", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync("/missions", request, Ct)).StatusCode);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void The_hr_is_included_in_budget_checks_and_profiles_have_bounded_skills()
    {
        Assert.Equal(["_rh"], AgentsModule.RolesFor(MissionKind.Recruitment));
        Assert.Null(RecruitmentAssistant.Validate(Profile));
        Assert.NotNull(RecruitmentAssistant.Validate(Profile with { Skills = [] }));
        Assert.NotNull(RecruitmentAssistant.Validate(Profile with { Tools = ["grant_admin"] }));
    }
}
