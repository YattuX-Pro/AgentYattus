using AgentYattus.Api.Modules.Agents;
using LibGit2Sharp;

namespace AgentYattus.Api.Tests;

/// <summary>La partie sans modèle de la mission Développer (ADR 0016) : lire une commande, comparer, lire un verdict.</summary>
public sealed class DevelopChecksTests
{
    private static readonly RepositoryCheck Test = new("tester", "dotnet test", "Orders.slnx");

    private static CheckRun Run(CommandResult? result) => DevelopChecks.Classify(Test, result, TimeSpan.FromSeconds(3));

    [Fact]
    public void A_command_result_is_passed_failed_or_unavailable_never_guessed()
    {
        Assert.Equal(CheckOutcome.Passed, Run(new(0, "ok", false)).Outcome);
        Assert.Equal(CheckOutcome.Failed, Run(new(1, "1 test en échec", false)).Outcome);
        Assert.Equal(CheckOutcome.Failed, Run(new(-1, "", TimedOut: true)).Outcome);
        // Pas de Docker, une commande absente de l'image, des paquets à télécharger sans réseau : rien n'a été vérifié.
        Assert.Equal(CheckOutcome.Unavailable, Run(null).Outcome);
        Assert.Equal(CheckOutcome.Unavailable, Run(new(127, "sh: npm: not found", false)).Outcome);
        Assert.Equal(CheckOutcome.Unavailable, Run(new(1, "error NU1301: Unable to load the service index", false)).Outcome);
    }

    [Fact]
    public void After_is_compared_with_before_to_tell_a_regression_from_an_old_failure()
    {
        CheckRun passed = Run(new(0, "", false)), failed = Run(new(1, "", false)), missing = Run(null);

        Assert.Equal("régression", DevelopChecks.Compare(passed, failed));
        Assert.Equal("échec déjà présent avant", DevelopChecks.Compare(failed, failed));
        Assert.Equal("corrigé", DevelopChecks.Compare(failed, passed));
        Assert.Equal("réussi", DevelopChecks.Compare(passed, passed));
        Assert.Equal("échec", DevelopChecks.Compare(null, failed));
        Assert.Equal("non vérifié", DevelopChecks.Compare(passed, missing));
    }

    [Theory]
    [InlineData("VERDICT : ACCEPTÉ\nRien à redire.", "Accepted")]
    [InlineData("Verdict : à corriger\n- a.cs:3 oubli", "ChangesRequested")]
    [InlineData("**VERDICT : A CORRIGER**", "ChangesRequested")]
    [InlineData("Ça me semble bien.", "Inconclusive")]
    [InlineData(null, "Inconclusive")]
    public void The_review_verdict_is_read_from_its_first_lines(string? review, string expected) =>
        Assert.Equal(expected, DevelopChecks.Verdict(review).ToString());

    [Fact]
    public void Writing_into_git_is_refused_and_a_worktree_git_file_is_read_only_in_the_sandbox()
    {
        var root = Directory.CreateTempSubdirectory("worktree-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: /ailleurs/.git/worktrees/agent-1");
            var tools = new WorkspaceTools(root, isolated: true);

            Assert.ThrowsAny<UnauthorizedAccessException>(() => tools.WriteFile(".git", "gitdir: /autre", TestContext.Current.CancellationToken).GetAwaiter().GetResult());
            Assert.ThrowsAny<UnauthorizedAccessException>(() => tools.WriteFile(".GIT", "x", TestContext.Current.CancellationToken).GetAwaiter().GetResult());
            var args = DockerSandbox.Arguments("n", root, "ls", new AgentsOptions(), null);
            Assert.Contains($"type=bind,source={Path.Combine(root, ".git")},target=/work/.git,readonly", args);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void In_an_isolated_worktree_writing_and_commands_need_no_approval_but_elsewhere_they_do()
    {
        var root = Directory.CreateTempSubdirectory("isole-").FullName;
        try
        {
            static string[] Guarded(WorkspaceTools tools) =>
                [.. tools.AsAIFunctions().Where(t => t is Microsoft.Extensions.AI.ApprovalRequiredAIFunction).Select(t => t.Name)];

            Assert.Empty(Guarded(new WorkspaceTools(root, isolated: true)));
            Assert.Equal(["write_file", "edit_file", "run_command", "write_document"], Guarded(new WorkspaceTools(root)));
            // Seule une copie de travail créée par le code, sous le dossier des copies, compte comme isolée.
            Assert.False(AgentFrameworkRunner.IsMissionWorktree(root, Path.Combine(root, "copies")));
            Repository.Init(root);
            Assert.True(AgentFrameworkRunner.IsMissionWorktree(root, Path.GetDirectoryName(root)!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
