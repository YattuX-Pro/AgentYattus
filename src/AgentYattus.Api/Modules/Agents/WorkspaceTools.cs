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
    private readonly ICommandSandbox? _sandbox;

    /// <param name="excluded">Motifs de noms (« .env », « *.pfx ») : ces fichiers et dossiers n'existent pas pour l'agent (ADR 0010).</param>
    public WorkspaceTools(string rootPath, long maxFileBytes = 256 * 1024, IReadOnlyList<string>? excluded = null, ICommandSandbox? sandbox = null)
    {
        _sandbox = sandbox;
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
        ("search", "Cherche un texte dans les fichiers du workspace et rend les lignes trouvées (fichier:ligne: texte).", false),
        ("edit_file", "Remplace un passage exact d'un fichier par un autre ; le passage doit apparaître une seule fois. Un humain doit approuver.", true),
        ("run_command", "Lance une commande (tests, compilation) dans un conteneur isolé, sans réseau, dans le dossier du projet. Un humain doit approuver chaque commande.", true),
        ("write_document", "Crée ou remplace un document Word (.docx) du workspace, à partir d'un texte : « # » pour un titre, « - » pour une puce. Un humain doit approuver.", true),
    ];

    public IReadOnlyList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(ListFiles, Catalog[0].Name, Catalog[0].Description),
        AIFunctionFactory.Create(ReadFile, Catalog[1].Name, Catalog[1].Description),
        // Écrire modifie le disque de l'utilisateur : la boucle s'arrête et demande son accord avant chaque appel.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteFile, Catalog[2].Name, Catalog[2].Description)),
        AIFunctionFactory.Create(Search, Catalog[3].Name, Catalog[3].Description),
        // Modifier, c'est écrire : la même règle que write_file.
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(EditFile, Catalog[4].Name, Catalog[4].Description)),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(RunCommand, Catalog[5].Name, Catalog[5].Description)),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteDocument, Catalog[6].Name, Catalog[6].Description)),
    ];

    /// <summary>
    /// Une commande (ADR 0015), toujours dans le bac à sable, jamais sur la machine. Sa sortie est une donnée :
    /// le modèle la lit comme le contenu d'un fichier.
    /// </summary>
    public async Task<string> RunCommand(
        [Description("La commande, lancée par sh -c dans /work (le dossier du projet). Exemple : dotnet test")] string command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 2000)
            throw new InvalidOperationException("Une commande de 1 à 2 000 caractères.");
        if (_sandbox is null || await _sandbox.RunAsync(_root, command, cancellationToken, ExcludedEntries()) is not { } result)
            throw new InvalidOperationException("Bac à sable indisponible : Docker n'est pas installé ou pas démarré sur le serveur.");
        var status = result.TimedOut ? "arrêtée : durée maximum dépassée" : $"code de sortie {result.ExitCode}";
        return $"Commande {status}.\n{result.Output}";
    }

    // Dossiers sans intérêt pour une recherche (dépendances, sorties de compilation, historique git).
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", "target", "packages" };

    /// <summary>
    /// Chercher dans le code (leçon 122) : un texte, sans tenir compte de la casse. Mêmes règles que list_files et read_file :
    /// pas de lien symbolique, pas de fichier exclu, pas de gros fichier. 100 lignes au plus, chacune coupée à 200 caractères.
    /// </summary>
    public string Search(
        [Description("Le texte cherché (pas une expression régulière).")] string text,
        [Description("Le dossier où chercher, relatif à la racine du workspace.")] string path = ".",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
            throw new InvalidOperationException("Le texte cherché fait de 1 à 200 caractères.");
        var start = Resolve(path);
        if (!Directory.Exists(start))
            throw new DirectoryNotFoundException($"Dossier introuvable : {path}");
        var results = new List<string>();
        var visited = 0;
        var pending = new Stack<string>([start]);
        while (pending.Count > 0 && results.Count < 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (IsLink(entry) || MatchesExcluded(name))
                    continue;
                if (Directory.Exists(entry))
                {
                    if (!SkippedFolders.Contains(name))
                        pending.Push(entry);
                    continue;
                }
                // 5 000 fichiers au plus : une recherche ne parcourt pas tout un disque.
                if (++visited > 5000 || new FileInfo(entry).Length > _maxFileBytes)
                    continue;
                var number = 0;
                foreach (var line in File.ReadLines(entry))
                {
                    number++;
                    if (line.Contains('\0'))
                        break; // un fichier binaire
                    if (!line.Contains(text, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var shown = line.Trim();
                    results.Add($"{Path.GetRelativePath(_root, entry)}:{number}: {(shown.Length > 200 ? shown[..200] + "…" : shown)}");
                    if (results.Count >= 100)
                        break;
                }
                if (results.Count >= 100)
                    break;
            }
        }
        return results.Count == 0 ? "Aucun résultat." : string.Join('\n', results) + (results.Count >= 100 ? "\n[… 100 résultats au plus]" : "");
    }

    /// <summary>
    /// Les fichiers et dossiers exclus présents dans le dossier (chemins relatifs), pour les masquer dans le bac à sable.
    /// Tout le dossier est parcouru, dépendances et .git compris : un secret peut s'y cacher.
    /// Plus de 200 à masquer, ou plus de 50 000 entrées à parcourir : la commande est refusée.
    /// </summary>
    internal IReadOnlyList<string> ExcludedEntries()
    {
        var found = new List<string>();
        if (_excluded.Count == 0)
            return found;
        var pending = new Stack<string>([_root]);
        var seen = 0;
        while (pending.Count > 0 && found.Count <= DockerSandbox.MaxHidden)
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++seen > 50_000)
                    throw new InvalidOperationException("Dossier trop grand pour vérifier ses fichiers exclus (plus de 50 000 entrées) : commande refusée.");
                if (IsLink(entry))
                    continue;
                if (MatchesExcluded(Path.GetFileName(entry)))
                    found.Add(Path.GetRelativePath(_root, entry));
                else if (Directory.Exists(entry))
                    pending.Push(entry);
            }
        return found;
    }

    /// <summary>
    /// Modifier un passage (leçon 123) : plus sûr qu'une réécriture complète. L'humain relit seulement ce qui change,
    /// et un passage absent ou ambigu (plusieurs fois présent) est refusé plutôt que deviné.
    /// </summary>
    public async Task<string> EditFile(
        [Description("Chemin du fichier, relatif à la racine du workspace.")] string path,
        [Description("Le passage exact à remplacer, espaces et retours à la ligne compris.")] string oldText,
        [Description("Le nouveau passage.")] string newText,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Resolve(path));
        if (!file.Exists)
            throw new FileNotFoundException($"Fichier introuvable : {path}");
        if (file.Length > _maxFileBytes)
            throw new InvalidOperationException($"Fichier trop volumineux : {file.Length} octets (maximum {_maxFileBytes}).");
        if (string.IsNullOrEmpty(oldText))
            throw new InvalidOperationException("Le passage à remplacer est vide.");
        var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
        var first = content.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0)
            throw new InvalidOperationException("Passage introuvable : relis le fichier et recopie le passage exactement.");
        if (content.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Passage présent plusieurs fois : ajoute des lignes autour pour qu'il soit unique.");
        var updated = string.Concat(content.AsSpan(0, first), newText, content.AsSpan(first + oldText.Length));
        if (System.Text.Encoding.UTF8.GetByteCount(updated) > _maxFileBytes)
            throw new InvalidOperationException($"Contenu trop volumineux (maximum {_maxFileBytes} octets).");
        await File.WriteAllTextAsync(file.FullName, updated, cancellationToken);
        return $"Fichier modifié : {path}";
    }

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
