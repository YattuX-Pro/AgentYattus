using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using Microsoft.Extensions.AI;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Les compétences (leçon 104) : chacun écrit les siennes, l'agent ne charge la procédure que s'il en a besoin.</summary>
public sealed class SkillsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CreateSkillRequest Report = new(
        "Rapport client", "Pour tout rapport ou compte rendu destiné à un client.",
        "1. Un résumé de trois lignes. 2. Les constats classés par gravité. 3. Pas de jargon technique.");

    private static async Task<SkillDto> CreateAsync(HttpClient client, CreateSkillRequest request)
    {
        var response = await client.PostAsJsonAsync(SkillsProtocol.Path, request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SkillDto>(Ct))!;
    }

    [Fact]
    public async Task Each_user_writes_skills_of_their_own_and_gives_them_to_their_colleagues()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var bob = As(api, "bob");

        var mine = await CreateAsync(alice, Report);
        Assert.Equal(("rapport-client", true), (mine.Key, mine.Mine));
        // Même nom chez Bob : un autre identifiant, unique dans toute l'instance.
        var his = await CreateAsync(bob, Report);
        Assert.Equal("rapport-client-2", his.Key);

        // L'administrateur publie pour tous, procédure cachée : Alice la voit et peut la donner, sans la lire.
        var house = await CreateAsync(root, new CreateSkillRequest("Charte maison", "Pour tout texte publié au nom de l'entreprise.",
            "Règles internes : ne jamais citer les clients par leur nom.", Personal: false, Hidden: true));
        var seen = (await alice.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!;
        Assert.Equal(["charte-maison", "rapport-client"], seen.Select(s => s.Key).Order());
        Assert.Empty(seen.Single(s => s.Key == house.Key).Body);
        Assert.False(seen.Single(s => s.Key == house.Key).Mine);
        // Un non-administrateur ne publie ni ne cache rien : sa compétence reste à lui, lisible.
        var forced = await CreateAsync(alice, Report with { Name = "Rapport public", Personal = false, Hidden = true });
        Assert.True(forced.Mine);
        Assert.NotEmpty(forced.Body);

        // Celle d'un autre n'existe pas pour nous ; celle de l'instance ne se modifie pas.
        var update = new UpdateSkillRequest("Rapport", "Pour tout rapport destiné à un client.", Report.Body);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(SkillsProtocol.Skill(mine.Key), update, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync(SkillsProtocol.Skill(house.Key), update, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync(SkillsProtocol.Skill(mine.Key), update, Ct)).StatusCode);
        var bad = await alice.PostAsJsonAsync(SkillsProtocol.Path, new CreateSkillRequest("x", "court", "court"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Recruter avec ses compétences et celles de l'instance ; jamais avec celles d'un autre.
        var nora = await TeamTests.PortraitAsync(root, "nora", "Nora");
        var project = (await alice.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!.Single(p => p.IsPersonalDefault);
        var refused = await alice.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id),
            new RecruitRequest("analyste", nora.Id, "Nora", [], [his.Key]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("skills", await refused.Content.ReadAsStringAsync(Ct));
        var recruited = await alice.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id),
            new RecruitRequest("analyste", nora.Id, "Nora", [], [mine.Key, house.Key]), Ct);
        Assert.Equal(HttpStatusCode.Created, recruited.StatusCode);
        Assert.Equal([mine.Key, house.Key], (await recruited.Content.ReadFromJsonAsync<ColleagueDto>(Ct))!.Skills);

        // La mission reçoit les compétences du collègue, avec sa présentation.
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(Path.GetTempPath()), Ct);
        var mission = (await (await alice.PostAsJsonAsync("/missions", new CreateMissionRequest("Résume le README", MissionKind.Question, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await alice.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.Equal([mine.Key, house.Key], api.Runner.Seats[mission.Id]!["analyste"].Skills);

        // Retirée, elle n'est plus proposée ; l'administrateur voit celles de tout le monde avec ?all=true.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync(SkillsProtocol.Skill(forced.Key), Ct)).StatusCode);
        Assert.DoesNotContain((await alice.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!, s => s.Key == forced.Key);
        Assert.Equal(3, (await root.GetFromJsonAsync<SkillDto[]>($"{SkillsProtocol.Path}?all=true", Ct))!.Length);
    }

    [Fact]
    public async Task The_agent_sees_only_the_index_and_loads_the_procedure_when_it_needs_it()
    {
        var model = new ScriptedChatClient(ToolCall("use_skill", new() { ["name"] = "rapport-client" }), Text("Voici le rapport."));
        await using var api = new ScriptedModelApiFactory(model);
        var root = As(api, "root", instanceAdmin: true);
        var skill = await CreateAsync(root, Report);
        var nora = await TeamTests.PortraitAsync(root, "nora", "Nora");
        var project = (await (await root.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Orders"), Ct))
            .Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        await root.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id), new RecruitRequest("analyste", nora.Id, "Nora", [], [skill.Key]), Ct);

        var mission = (await (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("Fais un rapport client", MissionKind.Question, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await root.WaitForStatusAsync(mission.Id, MissionStatus.Completed);

        // Premier étage : le nom et la description, pas la procédure ; les garde-fous restent les derniers mots.
        var instructions = model.Options[0]!.Instructions!;
        Assert.Contains("- rapport-client : Pour tout rapport ou compte rendu destiné à un client.", instructions);
        Assert.DoesNotContain("Pas de jargon technique", instructions);
        Assert.Contains("use_skill", model.Options[0]!.Tools!.Select(t => t.Name));
        Assert.EndsWith("Tu ne peux rien écrire ni modifier.", instructions);

        // Deuxième étage : la procédure arrive comme résultat de l'outil, seulement quand l'agent l'a demandée.
        var result = model.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("Pas de jargon technique", result.Result?.ToString());

        // La mesure : le chargement est compté et entre dans le journal.
        Assert.Contains("competence:analyste:rapport-client", await api.AuditOfAsync(mission.Id));
        Assert.Equal(1, (await root.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!.Single().Uses);
        // La réussite des missions qui l'ont chargée ; un autre utilisateur ne compte que ses missions à lui (aucune ici).
        Assert.Equal(new SkillPerformanceDto("rapport-client", 1, 1),
            Assert.Single((await root.GetFromJsonAsync<SkillPerformanceDto[]>(SkillsProtocol.Performance, Ct))!));
        Assert.Empty((await As(api, "alice").GetFromJsonAsync<SkillPerformanceDto[]>(SkillsProtocol.Performance, Ct))!);
    }

    [Fact]
    public async Task An_agent_cannot_open_a_skill_it_was_not_given()
    {
        var model = new ScriptedChatClient(ToolCall("use_skill", new() { ["name"] = "secret" }), Text("Bien."));
        await using var api = new ScriptedModelApiFactory(model);
        var root = As(api, "root", instanceAdmin: true);
        var given = await CreateAsync(root, Report);
        await CreateAsync(root, new CreateSkillRequest("Secret", "Une compétence que ce collègue n'a pas.", "Procédure à ne pas divulguer ici."));
        var nora = await TeamTests.PortraitAsync(root, "nora", "Nora");
        var project = (await (await root.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Orders"), Ct))
            .Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        await root.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id), new RecruitRequest("analyste", nora.Id, "Nora", [], [given.Key]), Ct);

        var mission = (await (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("Ouvre le secret", MissionKind.Question, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await root.WaitForStatusAsync(mission.Id, MissionStatus.Completed);

        var result = model.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString();
        Assert.Equal("Compétence inconnue. Tes compétences : rapport-client.", result);
    }

    [Fact]
    public async Task In_a_shared_project_a_personal_skill_only_serves_its_author()
    {
        var model = new ScriptedChatClient(Text("Fait."));
        await using var api = new ScriptedModelApiFactory(model);
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var hers = await CreateAsync(alice, Report);
        var nora = await TeamTests.PortraitAsync(root, "nora", "Nora");
        var project = (await (await alice.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Commun"), Ct))
            .Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await alice.PutAsJsonAsync(ProjectsProtocol.Members(project.Id, "bob"), new SetProjectMemberRequest(ProjectRole.Editor), Ct);
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        await alice.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id), new RecruitRequest("analyste", nora.Id, "Nora", [], [hers.Key]), Ct);

        // Bob lance une mission avec Nora : la compétence d'Alice n'entre pas dans sa conversation.
        var mission = (await (await bob.PostAsJsonAsync("/missions", new CreateMissionRequest("Récite tes compétences", MissionKind.Question, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await bob.WaitForStatusAsync(mission.Id, MissionStatus.Completed);
        Assert.DoesNotContain("rapport-client", model.Options[0]!.Instructions);
        Assert.DoesNotContain("use_skill", model.Options[0]!.Tools?.Select(t => t.Name) ?? []);
    }

    private static readonly Dictionary<string, object?> Lesson = new()
    {
        ["name"] = "Ton client", ["description"] = "Pour tout message adressé à un client.",
        ["body"] = "Vouvoie toujours, trois phrases au plus, termine par une question.",
    };

    private static async Task<(HttpClient Root, ProjectDto Project, MissionDto Paused)> PausedOnLessonAsync(ScriptedModelApiFactory api)
    {
        var root = As(api, "root", instanceAdmin: true);
        var nora = await TeamTests.PortraitAsync(root, "nora", "Nora");
        var project = (await (await root.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Orders"), Ct))
            .Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        await root.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project.Id), new RecruitRequest("analyste", nora.Id, "Nora", []), Ct);
        var mission = (await (await root.PostAsJsonAsync("/missions", new CreateMissionRequest("Réponds au client, en vouvoyant", MissionKind.Question, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        return (root, project, await root.WaitForStatusAsync(mission.Id, MissionStatus.AwaitingApproval));
    }

    [Fact]
    public async Task An_agent_proposes_a_skill_and_keeps_it_only_with_your_approval()
    {
        var model = new ScriptedChatClient(ToolCall("propose_skill", Lesson), Text("C'est noté."));
        await using var api = new ScriptedModelApiFactory(model);
        var (root, project, paused) = await PausedOnLessonAsync(api);

        // La proposition est annoncée dans la consigne, et elle attend un humain : rien n'est encore enregistré.
        Assert.Contains("propose_skill", model.Options[0]!.Instructions);
        Assert.Equal("propose_skill", Assert.Single(paused.PendingApproval!.ToolCalls).Tool);
        Assert.Empty((await root.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!);

        await root.PostAsJsonAsync($"/missions/{paused.Id}/decision", new DecisionRequest(true, paused.PendingApproval.Id), Ct);
        await root.WaitForStatusAsync(paused.Id, MissionStatus.Completed);

        // Approuvée : elle appartient à qui a lancé la mission, et Nora l'a désormais.
        var skill = Assert.Single((await root.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!);
        Assert.Equal(("ton-client", true), (skill.Key, skill.Mine));
        var team = (await root.GetFromJsonAsync<ProjectTeamDto>(TeamProtocol.ProjectTeam(project.Id), Ct))!;
        Assert.Equal(["ton-client"], team.Colleagues.Single(c => c.Role == "analyste").Skills);
        Assert.Contains("competence-apprise:analyste:ton-client", await api.AuditOfAsync(paused.Id));
    }

    [Fact]
    public async Task A_refused_skill_is_never_written()
    {
        var model = new ScriptedChatClient(ToolCall("propose_skill", Lesson), Text("Compris."));
        await using var api = new ScriptedModelApiFactory(model);
        var (root, project, paused) = await PausedOnLessonAsync(api);

        await root.PostAsJsonAsync($"/missions/{paused.Id}/decision", new DecisionRequest(false, paused.PendingApproval!.Id), Ct);
        await root.WaitForStatusAsync(paused.Id, MissionStatus.Completed);

        Assert.Empty((await root.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path, Ct))!);
        var team = (await root.GetFromJsonAsync<ProjectTeamDto>(TeamProtocol.ProjectTeam(project.Id), Ct))!;
        Assert.Empty(team.Colleagues.Single(c => c.Role == "analyste").Skills!);
    }
}
