using AgentYattus.Api.Modules.Agents;
using LibGit2Sharp;

namespace AgentYattus.Api.Tests;

/// <summary>git en lecture, par libgit2 (leçon 126) : jamais de programme externe, même si le dépôt le demande.</summary>
public sealed class GitToolsTests
{
    private readonly string _repo = Directory.CreateTempSubdirectory("yattus-git-").FullName;

    private void Commit(string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(_repo, file), content);
        using var repo = new Repository(_repo);
        Commands.Stage(repo, file);
        var me = new Signature("Nora", "nora@exemple.fr", DateTimeOffset.UtcNow);
        repo.Commit(message, me, me);
    }

    [Fact]
    public void Status_diff_and_log_describe_the_repository()
    {
        Repository.Init(_repo);
        Commit("a.cs", "var x = t.Result;\n", "Premier commit");
        File.WriteAllText(Path.Combine(_repo, "a.cs"), "var x = await t;\n");
        File.WriteAllText(Path.Combine(_repo, "b.cs"), "nouveau\n");
        var git = new GitTools(_repo, 10_000);

        Assert.Equal("modifié\ta.cs\najouté\tb.cs", git.Status());
        var diff = git.Diff("a.cs");
        Assert.Contains("-var x = t.Result;", diff);
        Assert.Contains("+var x = await t;", diff);
        Assert.Matches(@"^[0-9a-f]{7} \d{4}-\d{2}-\d{2} Nora : Premier commit\n$", git.Log());
    }

    [Fact]
    public void A_repository_configured_to_run_a_program_never_runs_it()
    {
        Repository.Init(_repo);
        Commit("a.cs", "x\n", "Premier");
        var marker = Path.Combine(_repo, "execute");
        // Une configuration piégée : avec la commande git, « status » lancerait ces programmes.
        File.AppendAllText(Path.Combine(_repo, ".git", "config"),
            $"[core]\n\tfsmonitor = touch {marker}\n[filter \"piege\"]\n\tclean = touch {marker}\n");
        File.WriteAllText(Path.Combine(_repo, ".gitattributes"), "*.cs filter=piege\n");
        File.WriteAllText(Path.Combine(_repo, "a.cs"), "y\n");

        var git = new GitTools(_repo, 10_000);
        git.Status();
        git.Diff();

        Assert.False(File.Exists(marker));
        Assert.Throws<InvalidOperationException>(() => new GitTools(Directory.CreateTempSubdirectory().FullName, 100).Status());
    }
}
