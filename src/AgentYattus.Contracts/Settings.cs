namespace AgentYattus.Contracts;

/// <summary>Les réglages de l'instance, tels qu'ils s'appliquent (valeurs choisies ou valeurs de départ). ADR 0010.</summary>
/// <param name="ExcludedFiles">Motifs de noms (« .env », « *.pfx ») : invisibles pour les agents.</param>
/// <param name="AuditRetentionDays">0 : tout garder.</param>
/// <param name="MonthlyBudget">En dollars ; null : pas de budget.</param>
public sealed record InstanceSettingsDto(
    int MaxIterations, int MaxDurationMinutes, int MaxConcurrentMissions, int MaxFileKb, IReadOnlyList<string> ExcludedFiles,
    int AuditRetentionDays, decimal? MonthlyBudget, int BudgetAlertPercent, bool BlockAtBudget, InstanceEnvironmentDto Environment);

/// <summary>Ce qui vient de la configuration du serveur et ne se change pas depuis l'admin.</summary>
public sealed record InstanceEnvironmentDto(string? Authority, string? OtlpEndpoint, string? DefaultWorkspace, bool SensitiveTelemetry);

public sealed record UpdateInstanceSettingsRequest(
    int MaxIterations, int MaxDurationMinutes, int MaxConcurrentMissions, int MaxFileKb, IReadOnlyList<string> ExcludedFiles,
    int AuditRetentionDays, decimal? MonthlyBudget, int BudgetAlertPercent, bool BlockAtBudget);

public static class SettingsProtocol
{
    public const string Path = "/settings";
}
