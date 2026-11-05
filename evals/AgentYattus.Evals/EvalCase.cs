using System.Text.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Evals;

/// <summary>Une mission de test et ce qu'on attend d'une bonne réponse.</summary>
public sealed record EvalCase(
    string Name,
    string Goal,
    MissionKind Kind,
    IReadOnlyList<string> MustMention,
    IReadOnlyList<string>? ReportMustMention = null)
{
    public static IReadOnlyList<EvalCase> Load() =>
        JsonSerializer.Deserialize<EvalCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cases.json")),
            JsonSerializerOptions.Web) ?? [];

    /// <summary>
    /// La mission est réussie si elle est terminée, cite tout ce qu'elle doit citer et, pour une revue,
    /// a écrit un REPORT.md qui contient ce qu'il doit contenir.
    /// </summary>
    // ponytail: notation par mots-clés, simple et gratuite ; un juge LLM quand il faudra noter la qualité de rédaction.
    public EvalVerdict Grade(MissionStatus status, string? result, string? report)
    {
        if (status != MissionStatus.Completed)
            return new(false, $"mission {status}");
        var missing = MustMention.Where(word => result?.Contains(word, StringComparison.OrdinalIgnoreCase) != true).ToList();
        if (missing.Count > 0)
            return new(false, $"réponse sans : {string.Join(", ", missing)}");
        if (Kind == MissionKind.Review)
        {
            if (report is null)
                return new(false, "REPORT.md non écrit");
            var missingInReport = (ReportMustMention ?? [])
                .Where(word => !report.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            if (missingInReport.Count > 0)
                return new(false, $"REPORT.md sans : {string.Join(", ", missingInReport)}");
        }

        return new(true, "ok");
    }
}

public sealed record EvalVerdict(bool Success, string Reason);
