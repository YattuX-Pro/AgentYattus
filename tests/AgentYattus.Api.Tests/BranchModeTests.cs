using System.Net.Http.Json;
using AgentYattus.Contracts;
using LibGit2Sharp;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Le mode branche (leçons 128 et 129) : la mission travaille à part, et ne commite que sur sa branche.</summary>
public sealed class BranchModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mission_on_a_branch_never_touches_the_current_branch_writes_freely_in_its_worktree_and_commits_only_with_approval()
    {
        var model = new ScriptedChatClient(
            ToolCall("write_file", new() { ["path"] = "a.txt", ["content"] = "corrigé" }, "c1"),
            ToolCall("git_commit", new() { ["message"] = "Corrige a.txt" }, "c2"),
            Text("C'est fait, sur la branche."));
        await using var api = new ScriptedModelApiFactory(model);
        api.Settings["Agents:WorktreesFolder"] = Directory.CreateTempSubdirectory("worktrees-").FullName;
        var root = As(api, "root", instanceAdmin: true);
        await root.GetAsync(IdentityProtocol.Me, Ct);
        Repository.Init(api.Workspace);
        using (var repo = new Repository(api.Workspace))
        {
            Commands.Stage(repo, "a.txt");
            var me = new Signature("Nora", "nora@exemple.fr", DateTimeOffset.UtcNow);
            repo.Commit("Départ", me, me);
        }
        var project = (await (await root.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Dépôt"), Ct)).Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(api.Workspace), Ct);
        // Un agent qui a l'écriture et le commit.
        var dev = (await (await root.PostAsJsonAsync(AgentsProtocol.Path, new CreateAgentRequest("Développeur", "Corrige le code.",
            "Tu es le Développeur. Tu corriges le code et tu enregistres ton travail.", ["read_file", "write_file", "git_commit"]), Ct))
            .Content.ReadFromJsonAsync<AgentDto>(Ct))!;
        var created = (await (await root.PostAsJsonAsync("/missions",
            new CreateMissionRequest("Corrige a.txt", MissionKind.Question, project.Id, dev.Key, OnBranch: true), Ct))
            .Content.ReadFromJsonAsync<MissionDto>(Ct))!;

        // Dans sa copie de travail (ADR 0016), l'écriture ne demande pas d'accord ; le commit, si : c'est la seule pause.
        var paused = await root.WaitForStatusAsync(created.Id, MissionStatus.AwaitingApproval);
        Assert.StartsWith("agent/", paused.Branch);
        Assert.Equal("git_commit", Assert.Single(paused.PendingApproval!.ToolCalls).Tool);
        await root.PostAsJsonAsync($"/missions/{created.Id}/decision", new DecisionRequest(true, paused.PendingApproval.Id), Ct);
        await root.WaitForStatusAsync(created.Id, MissionStatus.Completed);

        // La branche courante et le dossier du projet n'ont pas bougé ; la branche de la mission a son commit.
        Assert.Equal("contenu A", await File.ReadAllTextAsync(Path.Combine(api.Workspace, "a.txt"), Ct));
        using var check = new Repository(api.Workspace);
        Assert.Equal("Départ", check.Head.Tip.MessageShort);
        var branch = check.Branches[paused.Branch];
        Assert.Equal(("Corrige a.txt", "Agent Yattus"), (branch.Tip.MessageShort, branch.Tip.Author.Name));
        Assert.Equal("corrigé", ((Blob)branch.Tip["a.txt"].Target).GetContentText());
    }
}
