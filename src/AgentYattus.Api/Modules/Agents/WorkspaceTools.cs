using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>
/// Outils de fichiers confinés à un dossier racine, le workspace.
/// La vérification des chemins vit ici, dans le code exécuté, jamais dans les instructions données au modèle.
/// </summary>
internal sealed class WorkspaceTools
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
        // Écrire modifie le disque de l'utilisateur : la boucle s'arrête et demande son accord avant chaque appel.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            WriteFile, "write_file", "Crée ou remplace un fichier texte du workspace. Un humain doit approuver chaque écriture.")),
    ];

    public string[] ListFiles([Description("Chemin du dossier, relatif à la racine du workspace.")] string path = ".")
    {
        var directory = Resolve(path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Dossier introuvable : {path}");
        }

        return Directory.EnumerateFileSystemEntries(directory)
            .Where(entry => !IsLink(entry))
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

    public async Task<string> WriteFile(
        [Description("Chemin du fichier, relatif à la racine du workspace.")] string path,
        [Description("Contenu texte complet du fichier.")] string content,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        if (System.Text.Encoding.UTF8.GetByteCount(content) > _maxFileBytes)
        {
            throw new InvalidOperationException($"Contenu trop volumineux (maximum {_maxFileBytes} octets).");
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"'{path}' est un dossier.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return $"Fichier écrit : {path}";
    }

    private string Resolve(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var isInsideRoot = fullPath == _root
            || fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isInsideRoot)
        {
            throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' est hors du workspace.");
        }

        // Un lien symbolique du workspace peut pointer n'importe où (un dépôt cloné peut en contenir un vers ~/.ssh).
        // On refuse tout chemin qui en traverse un. La racine et ses dossiers parents viennent de la configuration du serveur.
        var current = _root;
        foreach (var part in Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (part != "." && IsLink(current))
            {
                throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' passe par un lien symbolique.");
            }
        }

        return fullPath;
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;
}
