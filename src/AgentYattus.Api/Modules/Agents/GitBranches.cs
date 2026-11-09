using LibGit2Sharp;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>Les copies de travail des missions, pour le module Missions : il ne voit pas la configuration des agents.</summary>
public interface IMissionWorktrees
{
    /// <summary>Crée la branche et sa copie de travail ; null si le dossier n'est pas un dépôt git avec un commit.</summary>
    (string Branch, string Path)? Create(string repositoryPath, Guid missionId);
}

internal sealed class MissionWorktrees(Microsoft.Extensions.Options.IOptions<AgentsOptions> options) : IMissionWorktrees
{
    public (string Branch, string Path)? Create(string repositoryPath, Guid missionId) =>
        GitBranches.Create(repositoryPath, options.Value.WorktreesRoot, missionId);
}

/// <summary>
/// Le mode branche (leçon 127) : une mission travaille dans une copie de travail git à elle (« worktree »), sur une
/// branche « agent/… » créée depuis le dernier commit. La branche courante du projet et ses fichiers ne bougent pas.
/// </summary>
public static class GitBranches
{
    public const string Prefix = "agent/";

    /// <summary>Crée la branche et sa copie de travail ; null si le dossier du projet n'est pas un dépôt git avec un commit.</summary>
    public static (string Branch, string Path)? Create(string repositoryPath, string worktreesRoot, Guid missionId)
    {
        if (!Repository.IsValid(repositoryPath))
            return null;
        using var repo = new Repository(repositoryPath);
        if (repo.Head.Tip is not { } tip)
            return null;
        var name = missionId.ToString("N")[^8..];
        var branch = repo.CreateBranch(Prefix + name, tip);
        var path = System.IO.Path.Combine(worktreesRoot, name);
        Directory.CreateDirectory(worktreesRoot);
        // Le nom du worktree est celui de la mission ; sa copie de travail est hors du dossier du projet.
        repo.Worktrees.Add(branch.CanonicalName, $"agent-{name}", path, isLocked: false);
        return (branch.FriendlyName, path);
    }

    /// <summary>Vrai si la copie de travail n'a aucun changement : rien n'a encore été modifié par la mission.</summary>
    internal static bool IsClean(string worktree)
    {
        using var repo = new Repository(worktree);
        return !repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false }).IsDirty;
    }

    /// <summary>
    /// Les changements de la mission par rapport à son commit de départ, fichiers nouveaux compris : ils sont indexés dans la
    /// copie de travail de la mission (jamais dans le dépôt de l'humain), puis comparés au commit. Le diff est borné.
    /// </summary>
    internal static GitChanges Changes(string worktree, int maxDiffChars)
    {
        using var repo = new Repository(worktree);
        Commands.Stage(repo, "*");
        var patch = repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.Index);
        var files = patch.Select(p => $"{Label(p.Status)} `{p.Path}` (+{p.LinesAdded} −{p.LinesDeleted})").ToList();
        var diff = patch.Content;
        return new GitChanges(repo.Head.FriendlyName, files, diff.Length > maxDiffChars ? diff[..maxDiffChars] + "\n[… diff coupé]" : diff);
    }

    private static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "ajouté",
        ChangeKind.Deleted => "supprimé",
        ChangeKind.Renamed => "renommé",
        _ => "modifié",
    };
}

/// <summary>Ce que la mission a changé : sa branche, ses fichiers (« modifié `a.cs` (+3 −1) ») et le diff, borné.</summary>
internal sealed record GitChanges(string Branch, IReadOnlyList<string> Files, string Diff);
