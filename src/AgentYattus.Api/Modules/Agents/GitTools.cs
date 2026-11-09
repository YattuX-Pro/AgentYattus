using System.ComponentModel;
using System.Text;
using LibGit2Sharp;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>
/// git en lecture (leçon 126) : l'état, les différences, l'historique du dépôt du projet. Par libgit2, jamais par la
/// commande git : un dépôt cloné peut contenir une configuration (filtres, fsmonitor) qui fait exécuter un programme
/// à « git status ». libgit2 n'exécute jamais de programme externe.
/// </summary>
internal sealed class GitTools(string workspace, int maxChars)
{
    public static IReadOnlyList<(string Name, string Description)> Catalog { get; } =
    [
        ("git_status", "Liste les fichiers modifiés, ajoutés ou supprimés dans le dépôt git du projet."),
        ("git_diff", "Montre les modifications du dépôt git du projet par rapport au dernier commit (format diff)."),
        ("git_log", "Montre les derniers commits du dépôt git du projet."),
        ("git_commit", "Enregistre toutes les modifications dans un commit, sur la branche de la mission (mode branche seulement). Un humain doit approuver."),
    ];

    public IReadOnlyList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(Status, Catalog[0].Name, Catalog[0].Description),
        AIFunctionFactory.Create(Diff, Catalog[1].Name, Catalog[1].Description),
        AIFunctionFactory.Create(Log, Catalog[2].Name, Catalog[2].Description),
        // Un commit reste dans l'historique : l'humain l'approuve, comme une écriture.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(Commit, Catalog[3].Name, Catalog[3].Description)),
    ];

    /// <summary>
    /// Le commit de la mission (leçon 128) : seulement sur une branche « agent/… », jamais sur la branche de l'humain.
    /// Toutes les modifications sont incluses ; l'auteur est « Agent Yattus ».
    /// </summary>
    public string Commit([Description("Le message du commit : une ligne de résumé, puis le détail.")] string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 2000)
            throw new InvalidOperationException("Un message de 1 à 2 000 caractères.");
        using var repo = Open();
        if (!repo.Head.FriendlyName.StartsWith(GitBranches.Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("git_commit ne sert qu'en mode branche : la branche courante du projet n'est pas celle de la mission.");
        Commands.Stage(repo, "*");
        if (!repo.RetrieveStatus().IsDirty)
            return "Rien à enregistrer : aucune modification.";
        var agent = new Signature("Agent Yattus", "agent@agent-yattus.local", DateTimeOffset.UtcNow);
        var commit = repo.Commit(message.Trim(), agent, agent);
        return $"Commit {commit.Sha[..7]} sur {repo.Head.FriendlyName}.";
    }

    public string Status()
    {
        using var repo = Open();
        var entries = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false })
            .Where(e => e.State != FileStatus.Unaltered).Take(500).ToList();
        return entries.Count == 0 ? "Aucune modification." : string.Join('\n', entries.Select(e => $"{Label(e.State)}\t{e.FilePath}"));
    }

    public string Diff([Description("Un fichier ou un dossier précis (facultatif).")] string? path = null)
    {
        using var repo = Open();
        var paths = string.IsNullOrWhiteSpace(path) ? null : new[] { path };
        // Les modifications du dossier, indexées ou non, par rapport au dernier commit.
        var patch = repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory, paths);
        var text = patch.Content;
        if (text.Length == 0)
            return "Aucune modification.";
        return text.Length > maxChars ? text[..maxChars] + "\n[… diff coupé]" : text;
    }

    public string Log([Description("Combien de commits (20 au plus).")] int count = 10)
    {
        using var repo = Open();
        var text = new StringBuilder();
        foreach (var commit in repo.Commits.Take(Math.Clamp(count, 1, 20)))
            text.Append($"{commit.Sha[..7]} {commit.Author.When:yyyy-MM-dd} {commit.Author.Name} : {commit.MessageShort}\n");
        return text.Length == 0 ? "Aucun commit." : text.ToString();
    }

    private Repository Open()
    {
        // Le dépôt du dossier du projet lui-même : on ne remonte pas vers un dépôt parent qui contiendrait d'autres projets.
        if (!Repository.IsValid(workspace))
            throw new InvalidOperationException("Le dossier du projet n'est pas un dépôt git.");
        return new Repository(workspace);
    }

    private static string Label(FileStatus state) => state switch
    {
        _ when state.HasFlag(FileStatus.NewInWorkdir) || state.HasFlag(FileStatus.NewInIndex) => "ajouté",
        _ when state.HasFlag(FileStatus.DeletedFromWorkdir) || state.HasFlag(FileStatus.DeletedFromIndex) => "supprimé",
        _ when state.HasFlag(FileStatus.RenamedInWorkdir) || state.HasFlag(FileStatus.RenamedInIndex) => "renommé",
        _ => "modifié",
    };
}
