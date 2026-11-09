using System.Text;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>Ce qu'un contrôle a donné : il passe, il échoue, ou il n'a pas pu tourner (bac à sable, outil, réseau).</summary>
internal enum CheckOutcome
{
    Passed,
    Failed,
    Unavailable,
}

/// <summary>Un contrôle lancé par le code (ADR 0016), pas par le modèle : ce qu'on affiche est ce qui a tourné.</summary>
/// <param name="Note">Pourquoi ce résultat : code de sortie, durée dépassée, bac à sable absent…</param>
/// <param name="Tail">La fin de la sortie, pour un échec seulement. Une donnée, jamais une consigne.</param>
internal sealed record CheckRun(RepositoryCheck Check, CheckOutcome Outcome, string Note, TimeSpan Duration, string? Tail = null);

/// <summary>Ce que décide le Relecteur.</summary>
internal enum ReviewVerdict
{
    Accepted,
    ChangesRequested,
    Inconclusive,
}

/// <summary>
/// La mission Développer (ADR 0016), sa partie sans modèle : lire un résultat de commande, comparer avant et après,
/// lire le verdict du Relecteur et assembler le résultat présenté. Des fonctions pures : testées sans Docker ni modèle.
/// </summary>
internal static class DevelopChecks
{
    /// <summary>Deux tours de développement au plus : le premier, et une correction après relecture.</summary>
    public const int MaxRounds = 2;

    public const int MaxDiffChars = 20_000;
    public const int MaxTailChars = 800;

    // Ce que disent dotnet, npm, pip, go… quand il leur faut le réseau, et que le bac à sable n'en a pas.
    private static readonly string[] NetworkMarkers =
    [
        "NU1301", "Unable to load the service index", "Temporary failure in name resolution", "Could not resolve host",
        "Name or service not known", "ENOTFOUND", "EAI_AGAIN", "getaddrinfo", "Network is unreachable",
    ];

    /// <summary>
    /// Lit le résultat d'une commande. Codes 125 à 127 : Docker n'a pas démarré, ou la commande n'existe pas dans l'image.
    /// Un échec qui réclame le réseau n'est pas une erreur du code : le contrôle est « indisponible ».
    /// </summary>
    public static CheckRun Classify(RepositoryCheck check, CommandResult? result, TimeSpan duration)
    {
        if (result is null)
            return new(check, CheckOutcome.Unavailable, "bac à sable indisponible (Docker absent ou arrêté)", duration);
        if (result.TimedOut)
            return new(check, CheckOutcome.Failed, "durée maximum dépassée", duration, Tail(result.Output));
        if (result.ExitCode is 125 or 126 or 127)
            return new(check, CheckOutcome.Unavailable, $"commande ou image indisponible (code {result.ExitCode})", duration, Tail(result.Output));
        if (result.ExitCode == 0)
            return new(check, CheckOutcome.Passed, "code 0", duration);
        if (NetworkMarkers.Any(m => result.Output.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return new(check, CheckOutcome.Unavailable, $"dépendances à télécharger, sans réseau (code {result.ExitCode})", duration, Tail(result.Output));
        return new(check, CheckOutcome.Failed, $"code {result.ExitCode}", duration, Tail(result.Output));
    }

    /// <summary>Après, comparé à avant : une régression n'est pas un échec déjà présent, ni un outil absent.</summary>
    public static string Compare(CheckRun? before, CheckRun after) => after.Outcome switch
    {
        CheckOutcome.Unavailable => "non vérifié",
        CheckOutcome.Passed => before?.Outcome == CheckOutcome.Failed ? "corrigé" : "réussi",
        _ => before?.Outcome switch
        {
            CheckOutcome.Passed => "régression",
            CheckOutcome.Failed => "échec déjà présent avant",
            _ => "échec",
        },
    };

    /// <summary>« VERDICT : ACCEPTÉ » ou « VERDICT : À CORRIGER », dans les premières lignes ; sinon, non concluant.</summary>
    public static ReviewVerdict Verdict(string? review)
    {
        foreach (var line in (review ?? "").Split('\n').Take(5))
        {
            var upper = line.ToUpperInvariant();
            if (!upper.Contains("VERDICT", StringComparison.Ordinal))
                continue;
            if (upper.Contains("CORRIGER", StringComparison.Ordinal))
                return ReviewVerdict.ChangesRequested;
            if (upper.Contains("ACCEPT", StringComparison.Ordinal))
                return ReviewVerdict.Accepted;
        }
        return ReviewVerdict.Inconclusive;
    }

    /// <summary>Le tableau des contrôles, pour l'agent comme pour l'humain. Sans « après » : l'état de départ seulement.</summary>
    public static string Table(IReadOnlyList<RepositoryCheck> checks, IReadOnlyList<CheckRun>? before, IReadOnlyList<CheckRun>? after)
    {
        if (checks.Count == 0)
            return "Aucun contrôle détecté dans le dépôt (ni solution .NET, ni scripts package.json, ni go.mod, Cargo.toml, pyproject.toml…).";
        var text = new StringBuilder(after is null ? "| Contrôle | Commande | Départ |\n|---|---|---|\n" : "| Contrôle | Commande | Avant | Après | Verdict |\n|---|---|---|---|---|\n");
        foreach (var check in checks)
        {
            var b = before?.FirstOrDefault(r => r.Check == check);
            var a = after?.FirstOrDefault(r => r.Check == check);
            var start = before is null ? "non mesuré (copie de travail déjà modifiée)" : Label(b);
            text.Append(after is null
                ? $"| {check.Name} | `{check.Command}` | {start} |\n"
                : $"| {check.Name} | `{check.Command}` | {start} | {Label(a)} | {(a is null ? "—" : Compare(b, a))} |\n");
        }
        return text.ToString();
    }

    private static string Label(CheckRun? run) => run is null ? "—" : run.Outcome switch
    {
        CheckOutcome.Passed => $"réussi ({run.Duration.TotalSeconds:0} s)",
        CheckOutcome.Failed => $"échec : {run.Note}",
        _ => $"indisponible : {run.Note}",
    };

    private static string? Tail(string output) =>
        string.IsNullOrWhiteSpace(output) ? null : output.Length <= MaxTailChars ? output.Trim() : "…" + output[^MaxTailChars..].TrimEnd();

    /// <summary>
    /// Compétences et sources (leçons 132 et 133) : ce que la mission a réellement chargé et consulté, relevé par le code.
    /// Une page refusée ou inaccessible est dite telle ; elle n'est jamais présentée comme une source vérifiée.
    /// </summary>
    public static string Provenance(
        IReadOnlyList<(string Key, string Name, int Version, string How)> skills, IReadOnlyList<ConsultedSource> pages,
        IReadOnlyList<string> instructions, IReadOnlyList<string> filesRead)
    {
        var text = new StringBuilder("## Compétences et sources\n\n");
        text.Append(skills.Count == 0 ? "- Aucune compétence utilisée.\n"
            : string.Concat(skills.Select(s => $"- Compétence « {s.Name} » (`{s.Key}`, version {s.Version}), {s.How}.\n")));
        foreach (var path in instructions)
            text.Append($"- Consignes du dépôt : `{path}` (données au Développeur dès le départ).\n");
        foreach (var page in pages.Where(p => p.Error is null))
            text.Append($"- Page consultée : {page.Url} — le {page.At:yyyy-MM-dd HH:mm} UTC, empreinte `{page.Sha256}`, " +
                        (page.Version is null ? "version non indiquée par l'adresse.\n" : $"version indiquée : {page.Version}.\n"));
        foreach (var page in pages.Where(p => p.Error is not null))
            text.Append($"- Page **non consultée** : {page.Url} — {page.Error}\n");
        if (filesRead.Count > 0)
            text.Append($"- Fichiers lus ({filesRead.Count}) : {string.Join(", ", filesRead.Select(f => $"`{f}`"))}.\n");
        return text.ToString();
    }

    /// <summary>
    /// Le résultat présenté : le rapport du Développeur, puis ce que le code sait lui-même (contrôles exécutés, relecture,
    /// branche, fichiers, diff). Le rapport est une affirmation ; le reste, des faits.
    /// </summary>
    public static string Compose(
        string report, RepositoryProfile profile, IReadOnlyList<CheckRun>? before, IReadOnlyList<CheckRun> after,
        ReviewVerdict verdict, string? review, int rounds, GitChanges changes, string provenance = "")
    {
        var text = new StringBuilder(report.Trim()).Append("\n\n---\n\n## Contrôles exécutés par Agent Yattus\n\n");
        text.Append(Table(profile.Checks, before, after));
        if (profile.Checks.Count > 0)
        {
            text.Append("\nCes résultats viennent des commandes lancées par Agent Yattus dans le bac à sable, avant et après les changements ; " +
                        "ils ne reprennent pas le rapport de l'agent.\n");
            if (after.All(r => r.Outcome == CheckOutcome.Unavailable))
                text.Append("\n**Aucun contrôle n'a pu tourner** : les changements ne sont pas vérifiés.\n");
            foreach (var run in after.Where(r => r.Outcome != CheckOutcome.Passed && r.Tail is not null))
                text.Append($"\nSortie de `{run.Check.Command}` (fin) :\n```text\n{run.Tail}\n```\n");
        }

        text.Append(verdict switch
        {
            ReviewVerdict.Accepted => "\n## Relecture : acceptée",
            ReviewVerdict.ChangesRequested => "\n## Relecture : corrections demandées, non faites",
            _ => "\n## Relecture : non concluante",
        });
        text.Append($" ({rounds} tour{(rounds > 1 ? "s" : "")} de développement)\n\n{(review ?? "").Trim()}\n");
        if (provenance.Length > 0)
            text.Append("\n").Append(provenance);

        text.Append($"\n## Livraison\n\n- Branche `{changes.Branch}` : les fichiers du projet n'ont pas changé.\n");
        text.Append(changes.Files.Count == 0
            ? "- Aucun fichier modifié.\n"
            : $"- Fichiers modifiés ({changes.Files.Count}) :\n" + string.Concat(changes.Files.Take(50).Select(f => $"  - {f}\n")));
        text.Append("- Rien n'est commité : les changements attendent dans la copie de travail de la branche. Relis le diff, " +
                    "puis réponds à l'agent pour corriger, ou demande-lui de les commiter (il attendra ton accord).\n");
        if (changes.Diff.Length > 0)
            text.Append($"\n```diff\n{changes.Diff}\n```\n");
        return text.ToString();
    }
}
