using System.ComponentModel;
using System.IO.Enumeration;
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
    private readonly IReadOnlyList<string> _excluded;

    /// <param name="excluded">Motifs de noms (« .env », « *.pfx ») : ces fichiers et dossiers n'existent pas pour l'agent (ADR 0010).</param>
    public WorkspaceTools(string rootPath, long maxFileBytes = 256 * 1024, IReadOnlyList<string>? excluded = null)
    {
        _excluded = excluded ?? [];
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace introuvable : {root}");
        }

        _root = Path.TrimEndingDirectorySeparator(root);
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>Les outils, décrits sans workspace : ce que l'écran Agents affiche.</summary>
    public static IReadOnlyList<(string Name, string Description, bool RequiresApproval)> Catalog { get; } =
    [
        ("list_files", "Liste les fichiers et dossiers d'un dossier du workspace.", false),
        ("read_file", "Lit le contenu texte d'un fichier du workspace.", false),
        ("write_file", "Crée ou remplace un fichier texte du workspace. Un humain doit approuver chaque écriture.", true),
    ];

    public IReadOnlyList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(ListFiles, Catalog[0].Name, Catalog[0].Description),
        AIFunctionFactory.Create(ReadFile, Catalog[1].Name, Catalog[1].Description),
        // Écrire modifie le disque de l'utilisateur : la boucle s'arrête et demande son accord avant chaque appel.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteFile, Catalog[2].Name, Catalog[2].Description)),
    ];

    public string[] ListFiles([Description("Chemin du dossier, relatif à la racine du workspace.")] string path = ".")
    {
        var directory = Resolve(path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Dossier introuvable : {path}");
        }

        return Directory.EnumerateFileSystemEntries(directory)
            .Where(entry => !IsLink(entry) && !IsExcluded(entry))
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

        // Un fichier exclu (secret probable) est refusé en lecture comme en écriture, et absent des listes.
        if (IsExcluded(fullPath))
        {
            throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' est exclu par la configuration.");
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

    /// <summary>
    /// Vrai si un des éléments du chemin (dossier ou fichier) correspond à un motif exclu.
    /// On compare le nom demandé ET le nom réel sur le disque : sur un disque insensible à la casse ou à la
    /// normalisation Unicode (macOS), « .ENV » ou un « é » décomposé désignent le même fichier que le motif.
    /// </summary>
    private bool IsExcluded(string fullPath)
    {
        if (_excluded.Count == 0 || fullPath == _root)
            return false;
        var current = _root;
        foreach (var raw in Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            // Flux de données alternatifs NTFS (« .env::$DATA ») et noms courts 8.3 (« ENV~1 ») : d'autres noms
            // pour un même fichier, sous Windows. Refusés d'office plutôt que de deviner à quoi ils mènent.
            if (raw.IndexOfAny([':', '~']) >= 0)
                return true;
            var part = raw.Normalize(System.Text.NormalizationForm.FormC);
            var actual = ActualName(current, part) ?? part;
            if (Matches(part) || Matches(actual))
                return true;
            current = Path.Combine(current, actual);
        }
        return false;

        bool Matches(string name) => _excluded.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));
    }

    /// <summary>Le nom tel qu'il est écrit sur le disque (casse, normalisation), ou null s'il n'existe pas.</summary>
    private static string? ActualName(string directory, string name) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)
                .FirstOrDefault(n => string.Equals(n!.Normalize(System.Text.NormalizationForm.FormC), name, StringComparison.OrdinalIgnoreCase))
            : null;
}
