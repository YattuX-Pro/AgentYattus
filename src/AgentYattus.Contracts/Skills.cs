namespace AgentYattus.Contracts;

/// <summary>Une compétence : un savoir-faire écrit qu'un agent ne charge que lorsque sa tâche en a besoin.</summary>
/// <param name="Key">Son identifiant (« rapport-client ») : le nom que l'agent donne à use_skill.</param>
/// <param name="Description">Quand s'en servir : la seule partie que l'agent voit avant de la charger.</param>
/// <param name="Body">La procédure ; vide si elle est cachée (publiée par l'administrateur) et que l'on n'est pas administrateur.</param>
/// <param name="Mine">Vrai pour une compétence à soi (modifiable) ; faux pour une compétence de l'instance.</param>
/// <param name="Uses">Combien de fois un agent l'a chargée.</param>
/// <param name="Version">Chaque modification crée une version ; une mission retient celle qu'elle a chargée (leçon 132).</param>
/// <param name="Status">Brouillon (importée, pas encore validée : aucun agent ne la lit), approuvée, ou dépréciée.</param>
/// <param name="Sources">Ses références : fichiers du dépôt et pages officielles, consultés par l'agent seulement au besoin.</param>
public sealed record SkillDto(
    string Key, string Name, string Description, string Body, bool Mine, bool Hidden, int Uses, DateTimeOffset? LastUsedAt,
    int Version = 1, SkillStatus Status = SkillStatus.Approved, IReadOnlyList<SkillSourceDto>? Sources = null);

/// <summary>Une compétence n'est lue par les agents qu'approuvée (ou dépréciée, signalée comme telle) ; retirée, elle n'existe plus.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SkillStatus>))]
// Approved en premier : c'est la valeur par défaut. EF n'écrit pas une valeur égale à celle du type (0), il prendrait celle
// de la base ; avec Draft en premier, une compétence importée en brouillon serait enregistrée approuvée.
public enum SkillStatus
{
    Approved,
    Draft,
    Deprecated,
}

/// <summary>Une référence d'une compétence : un fichier du dépôt (« depot ») ou une page officielle (« web », en https).</summary>
/// <param name="Note">Ce qu'on y trouve, ou la version de la technologie (« .NET 10 »).</param>
public sealed record SkillSourceDto(string Kind, string Location, string? Note = null)
{
    public const string Repository = "depot";
    public const string Web = "web";
}

/// <param name="Personal">Une compétence à soi, visible de son auteur seul. Toujours vrai pour un non-administrateur.</param>
/// <param name="Hidden">La procédure n'est montrée qu'à l'administrateur ; les agents la lisent quand même. Administrateur seulement.
/// Ce n'est pas une protection absolue : une procédure donnée au modèle peut être répétée par lui.</param>
public sealed record CreateSkillRequest(
    string Name, string Description, string Body, bool Personal = true, bool Hidden = false, IReadOnlyList<SkillSourceDto>? Sources = null);

public sealed record UpdateSkillRequest(string Name, string Description, string Body, IReadOnlyList<SkillSourceDto>? Sources = null);

/// <summary>Importer un fichier SKILL.md (son texte) : elle arrive en brouillon, à relire et valider (leçon 132).</summary>
public sealed record ImportSkillRequest(string Content, bool Personal = true);

/// <param name="Warnings">Ce qui mérite une relecture avant de valider : consignes risquées, outils mentionnés (aucun droit accordé).</param>
public sealed record SkillImportDto(SkillDto Skill, IReadOnlyList<string> Warnings);

public sealed record UpdateSkillStatusRequest(SkillStatus Status);

public sealed record SkillVersionDto(int Version, string Name, string Description, string Body, IReadOnlyList<SkillSourceDto> Sources, DateTimeOffset At, string By);

/// <summary>Ce que les missions disent d'une compétence (leçon 107) : combien l'ont chargée, combien ont abouti.</summary>
/// <param name="Missions">Missions terminées (réussies, échouées ou arrêtées) où un agent l'a chargée.</param>
public sealed record SkillPerformanceDto(string Skill, int Missions, int Completed);

public static class SkillsProtocol
{
    public const string Path = "/skills";

    /// <summary>Réussite des missions qui ont chargé chaque compétence (calculée par le module Missions).</summary>
    public const string Performance = "/skills/performance";

    public const string Import = "/skills/import";

    public static string Skill(string key) => $"/skills/{key}";
    public static string Status(string key) => $"/skills/{key}/status";
    public static string Versions(string key) => $"/skills/{key}/versions";
    public static string Restore(string key, int version) => $"/skills/{key}/versions/{version}/restore";
}
