using System.Net.Http.Json;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Contracts;
using LibGit2Sharp;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>
/// La mission Développer (ADR 0016), de bout en bout : un faux modèle, un faux bac à sable, un vrai dépôt git.
/// Le Développeur modifie sans accord dans sa copie de travail, le code lance les contrôles avant et après, le Relecteur juge.
/// </summary>
public sealed class DevelopMissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Report = "## Critères d'acceptation\n- ✅ a.txt dit B\n## Changements\n- a.txt : B au lieu de A\n## Vérifications\n- dotnet test\n## Points ouverts\n- aucun";

    /// <summary>Un bac à sable qui répond selon la commande et le passage (avant, puis après) ; null : pas de Docker.</summary>
    private sealed class FakeSandbox(Func<string, int, CommandResult?> answer) : ICommandSandbox
    {
        public List<string> Commands { get; } = [];

        public Task<CommandResult?> RunAsync(string workspace, string command, CancellationToken cancellationToken, IReadOnlyList<string>? hidden = null)
        {
            Commands.Add(command);
            return Task.FromResult(answer(command, Commands.Count(c => c == command)));
        }
    }

    /// <summary>Un projet dont le dossier est un dépôt git avec un commit : a.txt et une solution .NET (donc deux contrôles).</summary>
    private static async Task<(HttpClient Root, ProjectDto Project)> RepositoryAsync(ScriptedModelApiFactory api, bool git = true)
    {
        api.Settings["Agents:WorktreesFolder"] = Directory.CreateTempSubdirectory("worktrees-").FullName;
        var root = As(api, "root", instanceAdmin: true);
        await root.GetAsync(IdentityProtocol.Me, Ct);
        await File.WriteAllTextAsync(Path.Combine(api.Workspace, "App.slnx"), "<Solution />", Ct);
        if (git)
        {
            Repository.Init(api.Workspace);
            using var repo = new Repository(api.Workspace);
            Commands.Stage(repo, "*");
            var me = new Signature("Nora", "nora@exemple.fr", DateTimeOffset.UtcNow);
            repo.Commit("Départ", me, me);
        }
        var project = (await (await root.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Dépôt"), Ct)).Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        return (root, project);
    }

    private static async Task<MissionDto> DevelopAsync(HttpClient root, ProjectDto project, string goal)
    {
        var created = (await (await root.PostAsJsonAsync("/missions", new CreateMissionRequest(goal, MissionKind.Develop, project.Id), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;
        await root.WaitForFinalStatusAsync(created.Id);
        return (await root.GetFromJsonAsync<MissionDto>($"/missions/{created.Id}", Ct))!;
    }

    [Fact]
    public async Task The_developer_changes_its_own_worktree_the_code_runs_the_checks_and_the_reviewer_accepts()
    {
        var model = new ScriptedChatClient(
            ToolCall("edit_file", new() { ["path"] = "a.txt", ["oldText"] = "contenu A", ["newText"] = "contenu B" }, "c1"),
            Text(Report),
            Text("VERDICT : ACCEPTÉ\nLe critère est couvert par le diff."));
        // Le test échouait avant (1), il passe après (0) ; la compilation passe toujours.
        var sandbox = new FakeSandbox((command, pass) => new CommandResult(command.StartsWith("dotnet test", StringComparison.Ordinal) && pass == 1 ? 1 : 0, "", false));
        await using var api = new ScriptedModelApiFactory(model) { Sandbox = sandbox };
        var (root, project) = await RepositoryAsync(api);

        var mission = await DevelopAsync(root, project, "a.txt doit dire B");

        Assert.Equal(MissionStatus.Completed, mission.Status);
        Assert.StartsWith("agent/", mission.Branch);
        // Les fichiers du projet n'ont pas bougé : le changement est dans la copie de travail de la mission, sans accord demandé.
        Assert.Equal("contenu A", await File.ReadAllTextAsync(Path.Combine(api.Workspace, "a.txt"), Ct));
        var audit = await api.AuditOfAsync(mission.Id);
        Assert.DoesNotContain(audit, e => e.StartsWith("approbation-demandee", StringComparison.Ordinal));
        Assert.Equal(["dotnet build App.slnx", "dotnet test App.slnx", "dotnet build App.slnx", "dotnet test App.slnx"], sandbox.Commands);
        Assert.Contains("controle:avant:tester:failed", audit);
        Assert.Contains("controle:apres:tester:passed", audit);
        Assert.Contains("relecture:acceptee", audit);

        // Le résultat : le rapport de l'agent, puis ce que le code sait lui-même.
        var result = mission.Result!;
        Assert.StartsWith("## Critères d'acceptation", result);
        Assert.Contains("| tester | `dotnet test App.slnx` | échec : code 1 | réussi (0 s) | corrigé |", result);
        Assert.Contains("## Relecture : acceptée (1 tour de développement)", result);
        Assert.Contains("modifié `a.txt` (+1 −1)", result);
        Assert.Contains("+contenu B", result);

        // Le Développeur a le profil du dépôt et l'état de départ, ses outils d'écriture, mais pas git_commit pendant les étapes ;
        // le Relecteur ne peut rien écrire, et reçoit le diff.
        Assert.Contains("dotnet test App.slnx", model.Requests[0][^1].Text);
        var developerTools = model.Options[0]!.Tools!.Select(t => t.Name).Where(t => t != "poser_question").ToList();
        Assert.Contains("edit_file", developerTools);
        Assert.Contains("explore_repository", developerTools);
        Assert.DoesNotContain("git_commit", developerTools);
        Assert.Contains("copie de travail à part", model.Options[0]!.Instructions);
        Assert.Equal(["list_files", "read_file", "search", "git_diff", "read_documentation"], model.Options[2]!.Tools!.Select(t => t.Name).Where(t => t != "poser_question"));
        Assert.Contains("+contenu B", model.Requests[2][^1].Text);
    }

    [Fact]
    public async Task A_review_that_asks_for_corrections_sends_the_developer_back_once_and_a_regression_is_named()
    {
        var model = new ScriptedChatClient(
            Text(Report),
            Text("VERDICT : À CORRIGER\n- a.txt:1 le changement manque."),
            Text(Report + "\n(2e tour)"),
            Text("VERDICT : À CORRIGER\n- toujours pas."));
        // Le test passait avant et échoue après : une régression, pas un échec ancien.
        var sandbox = new FakeSandbox((command, pass) => new CommandResult(command.StartsWith("dotnet test", StringComparison.Ordinal) && pass > 1 ? 1 : 0, "Échec : OrderTests", false));
        await using var api = new ScriptedModelApiFactory(model) { Sandbox = sandbox };
        var (root, project) = await RepositoryAsync(api);

        var mission = await DevelopAsync(root, project, "a.txt doit dire B");

        var audit = await api.AuditOfAsync(mission.Id);
        // Deux tours au plus : la seconde demande de correction n'en déclenche pas un troisième.
        Assert.Equal(2, audit.Count(e => e == "agent:developpeur"));
        Assert.Equal(4, model.Requests.Count);
        Assert.Contains("La relecture demande des corrections", model.Requests[2][^1].Text);
        Assert.Contains("le changement manque", model.Requests[2][^1].Text);
        Assert.Contains("| régression |", mission.Result);
        Assert.Contains("## Relecture : corrections demandées, non faites (2 tours de développement)", mission.Result);
        Assert.Contains("Échec : OrderTests", mission.Result);
    }

    [Fact]
    public async Task Without_a_sandbox_nothing_is_presented_as_checked()
    {
        var model = new ScriptedChatClient(Text(Report), Text("VERDICT : ACCEPTÉ"));
        var sandbox = new FakeSandbox((_, _) => null);
        await using var api = new ScriptedModelApiFactory(model) { Sandbox = sandbox };
        var (root, project) = await RepositoryAsync(api);

        var mission = await DevelopAsync(root, project, "a.txt doit dire B");

        Assert.Equal(MissionStatus.Completed, mission.Status);
        Assert.Contains("**Aucun contrôle n'a pu tourner**", mission.Result);
        Assert.Contains("non vérifié", mission.Result);
        Assert.DoesNotContain("| réussi", mission.Result);
        // Le premier refus suffit : on n'essaie pas chaque contrôle sans Docker.
        Assert.Equal(2, sandbox.Commands.Count);
    }

    [Fact]
    public async Task A_develop_mission_needs_a_git_repository_and_never_touches_a_plain_folder()
    {
        var model = new ScriptedChatClient(Text("rien"));
        await using var api = new ScriptedModelApiFactory(model) { Sandbox = new FakeSandbox((_, _) => new CommandResult(0, "", false)) };
        var (root, project) = await RepositoryAsync(api, git: false);

        var mission = await DevelopAsync(root, project, "a.txt doit dire B");

        Assert.Equal(MissionStatus.Failed, mission.Status);
        Assert.Contains("dépôt git", mission.Result);
        Assert.Empty(model.Requests);
    }
}
