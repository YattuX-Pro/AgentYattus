using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

/// <summary>
/// Les dossiers de cet ordinateur, vus de l'accueil (leçon 128) : on les parcourt dans l'application, sans la boîte de
/// dialogue du système. Seulement de la lecture locale ; le serveur vérifie toujours le dossier choisi (ADR 0014).
/// </summary>
public static class Folders
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>« /Users/yatt/dev/orders-api » → « ~/dev/orders-api ».</summary>
    public static string Short(string path) =>
        Home.Length > 0 && path.StartsWith(Home, StringComparison.Ordinal)
        && (path.Length == Home.Length || path[Home.Length] is '/' or '\\')
            ? "~" + path[Home.Length..]
            : path;

    /// <summary>« orders-api » : le dernier nom du chemin.</summary>
    public static string Name(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>
    /// La branche courante d'un dépôt git de cet ordinateur, lue dans .git/HEAD ; null hors dépôt, sur un commit détaché,
    /// ou pour un chemin du serveur qui n'existe pas ici.
    /// </summary>
    public static string? BranchOf(string folder)
    {
        const string prefix = "ref: refs/heads/";
        try
        {
            var head = Path.Combine(folder, ".git", "HEAD");
            var line = File.Exists(head) ? File.ReadLines(head).FirstOrDefault() ?? "" : "";
            return line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..].Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Les sous-dossiers visibles (pas ceux qui commencent par « . »), par nom ; un dossier illisible n'en a aucun.</summary>
    public static IReadOnlyList<string> Children(string folder)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(folder)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Vrai si <paramref name="path"/> est l'une des racines ou se trouve dessous (la même règle que le serveur).</summary>
    public static bool Within(string path, IReadOnlyList<string> roots) =>
        roots.Select(Normal).Any(root => Normal(path) is var p
            && (p == root || p.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal)));

    private static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Le dossier personnel : le point de départ quand tout l'ordinateur est permis.</summary>
    public static string UserHome => Home;
}

/// <summary>Un sous-dossier, dans la navigation de l'accueil : son nom, et sa branche si c'est un dépôt git.</summary>
public sealed partial class FolderEntry(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = Folders.Name(path);
    public string? Branch { get; } = Folders.BranchOf(path);
    public bool IsRepository => Branch is not null;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
