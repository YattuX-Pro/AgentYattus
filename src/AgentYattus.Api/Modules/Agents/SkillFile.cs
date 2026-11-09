using System.Text.RegularExpressions;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>
/// Un fichier SKILL.md (leçon 132) : un en-tête « --- » (name, description, et nos champs facultatifs), puis la procédure en Markdown.
/// name et description sont ceux du format « Agent Skills » d'Anthropic ; references, technologies et tools sont à nous.
/// Seul le texte est lu : un dossier de compétence peut contenir des scripts, ils ne sont ni importés ni exécutés.
/// </summary>
internal sealed record SkillFile(string Name, string Description, string Body, IReadOnlyList<SkillSourceDto> Sources, IReadOnlyList<string> Notes)
{
    public const int MaxChars = 20_000;

    /// <summary>Null si ce n'est pas un SKILL.md lisible : trop long, sans en-tête, sans name ou sans description.</summary>
    public static SkillFile? Parse(string content)
    {
        if (content.Length is 0 or > MaxChars)
            return null;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines[0].Trim() != "---")
            return null;
        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
            return null;

        // Un sous-ensemble de YAML : « clé: valeur », et des listes « - élément » sous une clé vide. Rien d'autre n'est interprété.
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? listKey = null;
        foreach (var raw in lines[1..end])
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith('#') || line.Length == 0)
                continue;
            if (listKey is not null && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                lists[listKey].Add(Unquote(line.TrimStart()[2..]));
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0]))
                continue;
            var key = line[..colon].Trim();
            var value = Unquote(line[(colon + 1)..]);
            if (value.Length == 0)
                lists[listKey = key] = [];
            else
            {
                listKey = null;
                fields[key] = value;
            }
        }

        if (!fields.TryGetValue("name", out var name) || !fields.TryGetValue("description", out var description))
            return null;
        var sources = lists.GetValueOrDefault("references", []).Select(Source).ToList();
        var notes = new List<string>();
        if (lists.GetValueOrDefault("technologies") is { Count: > 0 } technologies)
            notes.Add($"Technologies concernées : {string.Join(", ", technologies)}.");
        var tools = lists.GetValueOrDefault("tools") ?? lists.GetValueOrDefault("allowed-tools");
        if (tools is { Count: > 0 } || fields.ContainsKey("allowed-tools"))
            notes.Add($"Outils mentionnés : {string.Join(", ", tools ?? [fields["allowed-tools"]])}. Une compétence ne donne aucun droit : l'agent garde les outils de son rôle.");
        return new SkillFile(name, description, string.Join('\n', lines[(end + 1)..]).Trim(), sources, notes);
    }

    // « depot: AGENTS.md » ou « web: https://… » ; une adresse seule est une page, un chemin seul un fichier du dépôt.
    private static SkillSourceDto Source(string item)
    {
        foreach (var kind in new[] { SkillSourceDto.Repository, SkillSourceDto.Web })
            if (item.StartsWith(kind + ":", StringComparison.OrdinalIgnoreCase) && !item.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
                return new(kind, item[(kind.Length + 1)..].Trim());
        return item.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new(SkillSourceDto.Web, item) : new(SkillSourceDto.Repository, item);
    }

    private static string Unquote(string value)
    {
        var v = value.Trim();
        return v.Length >= 2 && (v[0] == '"' && v[^1] == '"' || v[0] == '\'' && v[^1] == '\'') ? v[1..^1] : v;
    }
}

/// <summary>
/// Les contrôles avant de valider une compétence (leçon 132) : pas de secret (refusé), et les consignes qui méritent une relecture
/// (signalées). Une heuristique : elle aide la personne qui valide, elle ne la remplace pas.
/// </summary>
internal static partial class SkillReview
{
    /// <summary>Le motif du premier secret trouvé ; null s'il n'y en a pas. Une compétence va au modèle : jamais de secret dedans.</summary>
    public static string? Secrets(string text) =>
        SecretPattern().Match(text) is { Success: true } m
            ? $"Elle semble contenir un secret ({Describe(m.Value)}) : une compétence est lue par le modèle, retire-le."
            : null;

    private static string Describe(string value) =>
        value.StartsWith("-----", StringComparison.Ordinal) ? "clé privée" : value.Length > 8 ? value[..6] + "…" : "mot de passe";

    public static IReadOnlyList<string> Warnings(string body) =>
        [.. RiskyPatterns.Where(r => r.Pattern.IsMatch(body)).Select(r => r.Warning)];

    private static readonly (Regex Pattern, string Warning)[] RiskyPatterns =
    [
        (new(@"ignore[rsz]?\b.{0,40}\b(règles?|consignes?|instructions?)|ignore (all|previous|the) (rules|instructions)", RegexOptions.IgnoreCase),
            "Elle demande d'ignorer des règles : une compétence ne passe jamais avant les règles de sécurité ni la demande de l'humain."),
        (new(@"sans (demander|accord|validation)|without (asking|approval)", RegexOptions.IgnoreCase),
            "Elle parle d'agir sans accord : les accords restent imposés par le code."),
        (new(@"curl[^\n|]*\|\s*(ba)?sh|rm\s+-rf\s+[/~]|chmod\s+777", RegexOptions.IgnoreCase),
            "Elle contient une commande dangereuse (script téléchargé puis exécuté, suppression large, droits ouverts)."),
        (new(@"\.env\b|id_rsa|\.ssh/|secrets?\.json", RegexOptions.IgnoreCase),
            "Elle mentionne des fichiers de secrets : ils restent exclus pour les agents."),
    ];

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|sk-(ant-)?[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{30,}|xox[bpa]-[A-Za-z0-9-]{10,}|(password|mot de passe|passwd|pwd)\s*[:=]\s*\S{6,}", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
