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
        // Les motifs sous une seule forme Unicode (NFC), comme les noms auxquels on les compare.
        _excluded = (excluded ?? []).Select(p => p.Normalize(System.Text.NormalizationForm.FormC)).ToList();
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
        ("read_file", "Lit le contenu texte d'un fichier du workspace ; un PDF, un Word (.docx) ou un Excel (.xlsx) est converti en texte.", false),
        ("write_file", "Crée ou remplace un fichier texte du workspace. Un humain doit approuver chaque écriture.", true),
        ("write_document", "Crée ou remplace un document Word (.docx) du workspace, à partir d'un texte : « # » pour un titre, « - » pour une puce. Un humain doit approuver.", true),
    ];

    public IReadOnlyList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(ListFiles, Catalog[0].Name, Catalog[0].Description),
        AIFunctionFactory.Create(ReadFile, Catalog[1].Name, Catalog[1].Description),
        // Écrire modifie le disque de l'utilisateur : la boucle s'arrête et demande son accord avant chaque appel.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteFile, Catalog[2].Name, Catalog[2].Description)),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteDocument, Catalog[3].Name, Catalog[3].Description)),
    ];

    /// <summary>Un document Word (leçon 116) : l'humain approuve le texte, le code le met en page.</summary>
    public string WriteDocument(
        [Description("Chemin du document, relatif à la racine du workspace ; il finit par .docx.")] string path,
        [Description("Le texte du document : une ligne par paragraphe, « # » ou « ## » pour un titre, « - » pour une puce.")] string content)
    {
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Un document Word finit par .docx ; pour un autre fichier, utilise write_file.");
        var fullPath = Resolve(path);
        if (System.Text.Encoding.UTF8.GetByteCount(content) > _maxFileBytes)
            throw new InvalidOperationException($"Contenu trop volumineux (maximum {_maxFileBytes} octets).");
        if (Directory.Exists(fullPath))
            throw new InvalidOperationException($"'{path}' est un dossier.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Documents.WriteDocx(fullPath, content);
        return $"Document écrit : {path}";
    }

    public string[] ListFiles([Description("Chemin du dossier, relatif à la racine du workspace.")] string path = ".")
    {
        var directory = Resolve(path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Dossier introuvable : {path}");
        }

        return Directory.EnumerateFileSystemEntries(directory)
            // Les entrées lues ici sont les noms réels du disque : on les compare directement aux motifs.
            .Where(entry => !IsLink(entry) && !MatchesExcluded(Path.GetFileName(entry)))
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

        // Un document (leçon 114) : sa taille sur le disque peut dépasser la limite, c'est son texte qui est borné.
        if (Documents.IsDocument(file.Name))
        {
            if (file.Length > Documents.MaxDocumentBytes)
                throw new InvalidOperationException($"Document trop volumineux : {file.Length} octets (maximum {Documents.MaxDocumentBytes}).");
            try
            {
                // Hors du fil de la requête, avec son jeton : la mission peut être arrêtée pendant la lecture.
                return await Task.Run(() => Documents.ExtractText(file.FullName, (int)Math.Min(_maxFileBytes, int.MaxValue), cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Document illisible : {path} ({ex.GetType().Name}).");
            }
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
        if (_excluded.Count > 0)
        {
            CheckExclusions(relativePath, fullPath);
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
    /// Refuse un chemin dont un élément correspond à un motif exclu. Pour ne pas se laisser tromper par un autre nom
    /// du même fichier (casse, normalisation Unicode sur macOS, flux NTFS « :$DATA », nom court « ~1 » sous Windows),
    /// chaque élément doit être EXACTEMENT un nom présent sur le disque, octet pour octet, ou un nom nouveau qui
    /// ne ressemble à aucun nom existant. C'est ce nom exact qu'on compare aux motifs.
    /// </summary>
    private void CheckExclusions(string relativePath, string fullPath)
    {
        if (fullPath == _root)
            return;
        var current = _root;
        foreach (var part in Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            if (part.IndexOfAny([':', '~']) >= 0 || part.Any(c => char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Control))
            {
                throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' contient un caractère non autorisé (:, ~ ou caractère invisible).");
            }

            if (Directory.Exists(current))
            {
                // Un seul parcours du dossier par élément du chemin.
                var exists = false;
                var alias = false;
                var key = part.Normalize(System.Text.NormalizationForm.FormC);
                foreach (var name in Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName))
                {
                    if (string.Equals(name, part, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                    alias |= string.Equals(name!.Normalize(System.Text.NormalizationForm.FormC), key, StringComparison.OrdinalIgnoreCase);
                }
                if (!exists && alias)
                {
                    throw new UnauthorizedAccessException($"Accès refusé : '{part}' n'est pas le nom exact d'un fichier existant ; utilise le nom tel qu'il est listé.");
                }
            }

            if (MatchesExcluded(part))
            {
                throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' est exclu par la configuration.");
            }
            current = Path.Combine(current, part);
        }
    }

    private bool MatchesExcluded(string name)
    {
        var normalized = name.Normalize(System.Text.NormalizationForm.FormC);
        return _excluded.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, normalized, ignoreCase: true));
    }
}
