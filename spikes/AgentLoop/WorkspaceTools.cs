using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgentYattus.Spikes;

/// <summary>
/// Outils de fichiers confinés à un dossier racine, le workspace.
/// La vérification des chemins vit ici, dans le code exécuté, jamais dans les instructions données au modèle.
/// </summary>
public sealed class WorkspaceTools
{
    private readonly string _root;
    private readonly long _maxFileBytes;

    public WorkspaceTools(string rootPath, long maxFileBytes = 256 * 1024)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace introuvable : {root}");
        }

        _root = Path.TrimEndingDirectorySeparator(root);
        _maxFileBytes = maxFileBytes;
    }

    public IReadOnlyList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(ListFiles, "list_files", "Liste les fichiers et dossiers d'un dossier du workspace."),
        AIFunctionFactory.Create(ReadFile, "read_file", "Lit le contenu texte d'un fichier du workspace."),
    ];

    public string[] ListFiles([Description("Chemin du dossier, relatif à la racine du workspace.")] string path = ".")
    {
        var directory = Resolve(path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Dossier introuvable : {path}");
        }

        return Directory.EnumerateFileSystemEntries(directory)
            .Select(entry => Path.GetRelativePath(_root, entry))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> ReadFile(
        [Description("Chemin du fichier, relatif à la racine du workspace.")] string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Resolve(path));
        if (!file.Exists)
        {
            throw new FileNotFoundException($"Fichier introuvable : {path}");
        }

        if (file.Length > _maxFileBytes)
        {
            throw new InvalidOperationException($"Fichier trop volumineux : {file.Length} octets (maximum {_maxFileBytes}).");
        }

        return await File.ReadAllTextAsync(file.FullName, cancellationToken);
    }

    // ponytail: les liens symboliques ne sont pas résolus, un lien du workspace qui pointe ailleurs passerait.
    // À fermer dans le module Tools (phase 4) avec FileSystemInfo.ResolveLinkTarget.
    private string Resolve(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var isInsideRoot = fullPath == _root
            || fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isInsideRoot)
        {
            throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' est hors du workspace.");
        }

        return fullPath;
    }
}
