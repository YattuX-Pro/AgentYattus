using System.Text.RegularExpressions;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core;

/// <summary>Ce qui passe d'un bureau à l'autre pendant une réplique.</summary>
public enum SceneObject
{
    None,

    /// <summary>Un dossier, des archives (le workspace) vers l'analyste.</summary>
    Folder,

    /// <summary>Les constats, de l'analyste vers le rédacteur.</summary>
    Notes,

    /// <summary>Le brouillon à approuver, du rédacteur vers le bureau vitré.</summary>
    Report,
}

/// <summary>Une réplique de la scène : qui parle, ce qu'il dit, ce qu'il fait passer, et ce que chacun fait ensuite.</summary>
/// <param name="Speaker">« chef », « analyste », « redacteur » ou « vous ».</param>
/// <param name="Statuses">Le statut affiché sous chaque bureau après cette réplique (par rôle).</param>
public sealed record SceneLine(string Speaker, string Text, SceneObject Carries, IReadOnlyDictionary<string, string> Statuses, DateTimeOffset At, string? File = null);

/// <summary>
/// Traduit le journal d'une mission en répliques (ADR 0012). Le code décide qui parle et ce qui circule (le comportement) ;
/// le texte vient des répliques réglées dans l'admin (le contenu), reçues avec l'équipe du projet.
/// </summary>
public static partial class Scene
{
    public const string Leader = "chef";
    public const string Analyst = "analyste";
    public const string Writer = "redacteur";
    public const string You = "vous";

    /// <param name="you">Le prénom de la personne connectée, pour {vous}.</param>
    /// <param name="approvalFile">Le fichier que le rédacteur veut écrire (la demande en cours), pour {fichier}.</param>
    /// <param name="worker">L'agent qui fait une mission Question (ADR 0013) : il occupe le bureau de l'analyste.</param>
    public static IReadOnlyList<SceneLine> From(
        IReadOnlyList<AuditEntryDto> events, ProjectTeamDto team, string goal, string you, string? approvalFile = null, string worker = Analyst)
    {
        var names = new Dictionary<string, string>
        {
            [Leader] = team.LeaderName,
            [You] = you,
            [Analyst] = team.Colleagues.FirstOrDefault(c => c.Role == worker)?.Name ?? "l'analyste",
            [Writer] = team.Colleagues.FirstOrDefault(c => c.Role == Writer)?.Name ?? "le rédacteur",
        };
        var lines = new List<SceneLine>();
        var statuses = new Dictionary<string, string> { [Leader] = "disponible", [Analyst] = "disponible", [Writer] = "disponible" };
        var lastActor = Analyst;
        var folderBrought = false;
        string? writtenFile = null;
        // La demande en cours est une compétence à retenir (leçon 106), pas un fichier à écrire.
        var skillPending = false;

        void Say(string speaker, string @event, DateTimeOffset at, SceneObject carries = SceneObject.None, string? file = null, string? goalText = null)
        {
            if (!team.Lines.TryGetValue(@event, out var template))
                return;
            var values = new Dictionary<string, string>(names) { ["fichier"] = file ?? "", ["objectif"] = goalText ?? "" };
            lines.Add(new SceneLine(speaker, Fill(template, values), carries, new Dictionary<string, string>(statuses), at, file));
        }

        foreach (var e in events)
        {
            var (kind, rest) = Split(e.Type);
            switch (kind)
            {
                // Le premier départ seulement : une reprise après décision repasse aussi par « Running ».
                case "Running" when lines.Count == 0:
                    statuses[Leader] = "briefe l'équipe";
                    Say(Leader, "mission:debut", e.At, goalText: Shorten(goal, 80));
                    statuses[Leader] = "supervise";
                    break;
                case "agent" when rest == Analyst:
                    statuses[Analyst] = "prend la main";
                    Say(Leader, "agent:analyste", e.At);
                    break;
                case "agent" when rest == Writer:
                    statuses[Analyst] = "transmet ses constats";
                    statuses[Writer] = "reçoit les constats";
                    Say(Analyst, "passage", e.At, SceneObject.Notes);
                    statuses[Analyst] = "disponible";
                    statuses[Writer] = "rédige";
                    lastActor = Writer;
                    break;
                case "action":
                {
                    var parts = rest.Split(':', 3);
                    if (parts.Length < 3 || parts[1] is "write_file" or "write_document" or "edit_file" or "propose_skill")
                        break; // l'écriture et l'apprentissage se racontent avec la décision humaine
                    var (role, tool, path) = (parts[0], parts[1], parts[2]);
                    lastActor = role;
                    if (tool == "use_skill")
                    {
                        // Il ouvre une de ses fiches de compétence : rien ne voyage, il la lit à son bureau.
                        statuses[role] = "relit une fiche";
                        Say(role, "outil:use_skill", e.At, file: path);
                        break;
                    }
                    statuses[role] = tool switch
                    {
                        "read_file" => "lit le code",
                        "search" => "cherche dans le code",
                        "git_status" or "git_diff" or "git_log" => "regarde l'historique git",
                        "run_command" => "lance une commande",
                        _ => "parcourt le dossier",
                    };
                    // Le premier dossier vient des archives : le workspace est l'armoire du fond.
                    var carries = !folderBrought && role == worker ? SceneObject.Folder : SceneObject.None;
                    folderBrought |= carries == SceneObject.Folder;
                    Say(role, $"outil:{tool}", e.At, carries, path == "." ? "le dossier" : path);
                    break;
                }
                case "approbation-demandee" when rest == "propose_skill":
                    skillPending = true;
                    statuses[lastActor] = "attend votre accord";
                    Say(lastActor, "competence:proposee", e.At, file: approvalFile is { Length: > 0 } ? approvalFile : "nouvelle");
                    break;
                case "decision" when skillPending:
                    skillPending = false;
                    statuses[lastActor] = "disponible";
                    Say(lastActor, rest == "approuvee" ? "competence:retenue" : "decision:refusee", e.At);
                    break;
                case "approbation-demandee":
                    writtenFile = approvalFile is { Length: > 0 } ? Path.GetFileName(approvalFile) : "REPORT.md";
                    statuses[Writer] = "attend votre accord";
                    Say(Writer, "approbation", e.At, SceneObject.Report, writtenFile);
                    break;
                case "decision":
                    statuses[Writer] = rest == "approuvee" ? "écrit le fichier" : "reprend le brouillon";
                    Say(Writer, rest == "approuvee" ? "decision:approuvee" : "decision:refusee", e.At, file: writtenFile ?? "REPORT.md");
                    break;
                case "limite-de-tours":
                    Say(lastActor, "limite-de-tours", e.At);
                    break;
                case "Completed":
                    foreach (var key in statuses.Keys.ToList())
                        statuses[key] = "disponible";
                    Say(Leader, "mission:fin", e.At);
                    break;
                case "Failed":
                    Say(Leader, "mission:echec", e.At);
                    break;
                case "Cancelled":
                    Say(Leader, "mission:arret", e.At);
                    break;
            }
        }

        return lines;
    }

    /// <summary>Remplace chaque {variable} connue ; une variable inconnue reste telle quelle (l'admin les vérifie).</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        Variable().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static (string Kind, string Detail) Split(string type)
    {
        var i = type.IndexOf(':');
        return i < 0 ? (type, "") : (type[..i], type[(i + 1)..]);
    }

    private static string Shorten(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Variable();
}
