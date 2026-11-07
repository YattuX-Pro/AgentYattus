namespace AgentYattus.Contracts;

/// <summary>Une compétence : un savoir-faire écrit qu'un agent ne charge que lorsque sa tâche en a besoin.</summary>
/// <param name="Key">Son identifiant (« rapport-client ») : le nom que l'agent donne à use_skill.</param>
/// <param name="Description">Quand s'en servir : la seule partie que l'agent voit avant de la charger.</param>
/// <param name="Body">La procédure ; vide si elle est cachée (publiée par l'administrateur) et que l'on n'est pas administrateur.</param>
/// <param name="Mine">Vrai pour une compétence à soi (modifiable) ; faux pour une compétence de l'instance.</param>
/// <param name="Uses">Combien de fois un agent l'a chargée.</param>
public sealed record SkillDto(
    string Key, string Name, string Description, string Body, bool Mine, bool Hidden, int Uses, DateTimeOffset? LastUsedAt);

/// <param name="Personal">Une compétence à soi, visible de son auteur seul. Toujours vrai pour un non-administrateur.</param>
/// <param name="Hidden">La procédure n'est montrée qu'à l'administrateur ; les agents la lisent quand même. Administrateur seulement.</param>
public sealed record CreateSkillRequest(string Name, string Description, string Body, bool Personal = true, bool Hidden = false);

public sealed record UpdateSkillRequest(string Name, string Description, string Body);

/// <summary>Ce que les missions disent d'une compétence (leçon 107) : combien l'ont chargée, combien ont abouti.</summary>
/// <param name="Missions">Missions terminées (réussies, échouées ou arrêtées) où un agent l'a chargée.</param>
public sealed record SkillPerformanceDto(string Skill, int Missions, int Completed);

public static class SkillsProtocol
{
    public const string Path = "/skills";

    /// <summary>Réussite des missions qui ont chargé chaque compétence (calculée par le module Missions).</summary>
    public const string Performance = "/skills/performance";

    public static string Skill(string key) => $"/skills/{key}";
}
