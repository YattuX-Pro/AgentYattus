using System.Text.Json;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Settings;

/// <summary>
/// Les réglages qui s'appliquent à toutes les missions (ADR 0010). Lus à chaque mission : un changement vaut sans redémarrage.
/// </summary>
/// <param name="MaxIterations">Null : la valeur de la configuration (Agents:MaxIterations).</param>
public sealed record InstanceSettings(
    int? MaxIterations, int MaxDurationMinutes, int MaxConcurrentMissions, int MaxFileKb, IReadOnlyList<string> ExcludedFiles,
    int AuditRetentionDays, decimal? MonthlyBudget, int BudgetAlertPercent, bool BlockAtBudget);

public interface IInstanceSettings
{
    Task<InstanceSettings> GetAsync(CancellationToken cancellationToken);
}

public static class SettingsModule
{
    /// <summary>Les valeurs de départ, tant qu'un administrateur n'a rien choisi.</summary>
    public static readonly InstanceSettings Defaults = new(
        MaxIterations: null, MaxDurationMinutes: 15, MaxConcurrentMissions: 1, MaxFileKb: 256,
        // Des fichiers qui contiennent presque toujours des secrets : les agents ne les voient pas.
        ExcludedFiles: [".env", ".env.*", "*.pfx", "*.p12", "*.key", "*.pem", "id_rsa*", "id_ed25519*", "secrets.json"],
        AuditRetentionDays: 90, MonthlyBudget: null, BudgetAlertPercent: 80, BlockAtBudget: true);

    public static IServiceCollection AddSettingsModule(this IServiceCollection services) =>
        services.AddScoped<IInstanceSettings, InstanceSettingsStore>().AddModuleDb<SettingsDb>(SettingsDb.Schema);

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup(SettingsProtocol.Path).RequireAuthorization(Authentication.LlmAdminPolicy);

        settings.MapGet("/", async (IInstanceSettings store, IConfiguration configuration, CancellationToken ct) =>
            TypedResults.Ok(ToDto(await store.GetAsync(ct), configuration)));

        settings.MapPut("/", async Task<Results<NoContent, ValidationProblem>> (
            UpdateInstanceSettingsRequest request, SettingsDb db, HttpContext http, CancellationToken ct) =>
        {
            var excluded = (request.ExcludedFiles ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
            var errors = new Dictionary<string, string[]>();
            void Check(bool ok, string field, string message) { if (!ok) errors[field] = [message]; }
            Check(request.MaxIterations is >= 1 and <= 50, "maxIterations", "Entre 1 et 50 tours.");
            Check(request.MaxDurationMinutes is >= 1 and <= 240, "maxDurationMinutes", "Entre 1 et 240 minutes.");
            Check(request.MaxConcurrentMissions is >= 1 and <= 8, "maxConcurrentMissions", "Entre 1 et 8 missions.");
            Check(request.MaxFileKb is >= 16 and <= 4096, "maxFileKb", "Entre 16 et 4 096 Ko.");
            Check(request.AuditRetentionDays is >= 0 and <= 3650, "auditRetentionDays", "Entre 0 (tout garder) et 3 650 jours.");
            Check(request.MonthlyBudget is null or (>= 0.01m and <= 100_000m), "monthlyBudget", "Entre 0,01 et 100 000 $, ou aucun.");
            Check(request.BudgetAlertPercent is >= 1 and <= 100, "budgetAlertPercent", "Entre 1 et 100 %.");
            // Un motif désigne un nom de fichier ou de dossier, jamais un chemin : il ne peut pas viser l'extérieur.
            Check(excluded.Count <= 50 && excluded.All(p => p.Length <= 100 && p.IndexOfAny(['/', '\\']) < 0 && p != ".." && p != "."),
                "excludedFiles", "50 motifs au plus, chacun un nom (sans / ni \\), 100 caractères au plus.");
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            var row = await db.Settings.FindAsync([SettingsRow.SingletonId], ct);
            if (row is null)
                db.Settings.Add(row = new SettingsRow { Id = SettingsRow.SingletonId });
            row.MaxIterations = request.MaxIterations;
            row.MaxDurationMinutes = request.MaxDurationMinutes;
            row.MaxConcurrentMissions = request.MaxConcurrentMissions;
            row.MaxFileKb = request.MaxFileKb;
            row.ExcludedFiles = JsonSerializer.Serialize(excluded);
            row.AuditRetentionDays = request.AuditRetentionDays;
            row.MonthlyBudgetMicros = request.MonthlyBudget is { } b ? (long)(b * 1_000_000m) : null;
            row.BudgetAlertPercent = request.BudgetAlertPercent;
            row.BlockAtBudget = request.BlockAtBudget;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = http.User.Identity?.Name ?? "?";
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });
        return app;
    }

    private static InstanceSettingsDto ToDto(InstanceSettings s, IConfiguration configuration) => new(
        s.MaxIterations ?? configuration.GetValue("Agents:MaxIterations", 10), s.MaxDurationMinutes, s.MaxConcurrentMissions, s.MaxFileKb,
        s.ExcludedFiles, s.AuditRetentionDays, s.MonthlyBudget, s.BudgetAlertPercent, s.BlockAtBudget,
        new InstanceEnvironmentDto(
            configuration["Auth:Authority"], configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], configuration["Agents:Workspace"],
            // Le contenu des prompts et des réponses n'entre jamais dans les traces (EnableSensitiveData = false, leçon 21).
            SensitiveTelemetry: false));
}

internal sealed class InstanceSettingsStore(SettingsDb db) : IInstanceSettings
{
    public async Task<InstanceSettings> GetAsync(CancellationToken ct)
    {
        var d = SettingsModule.Defaults;
        if (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SettingsRow.SingletonId, ct) is not { } row)
            return d;
        return new InstanceSettings(
            row.MaxIterations ?? d.MaxIterations, row.MaxDurationMinutes ?? d.MaxDurationMinutes,
            row.MaxConcurrentMissions ?? d.MaxConcurrentMissions, row.MaxFileKb ?? d.MaxFileKb,
            row.ExcludedFiles is { } json ? JsonSerializer.Deserialize<List<string>>(json) ?? [] : d.ExcludedFiles,
            row.AuditRetentionDays ?? d.AuditRetentionDays,
            row.MonthlyBudgetMicros is { } micros ? micros / 1_000_000m : null,
            row.BudgetAlertPercent ?? d.BudgetAlertPercent, row.BlockAtBudget ?? d.BlockAtBudget);
    }
}

/// <summary>Une seule ligne ; chaque colonne nulle reprend la valeur de départ.</summary>
internal sealed class SettingsRow
{
    public const int SingletonId = 1;

    public int Id { get; set; }
    public int? MaxIterations { get; set; }
    public int? MaxDurationMinutes { get; set; }
    public int? MaxConcurrentMissions { get; set; }
    public int? MaxFileKb { get; set; }
    public string? ExcludedFiles { get; set; }
    public int? AuditRetentionDays { get; set; }
    public long? MonthlyBudgetMicros { get; set; }
    public int? BudgetAlertPercent { get; set; }
    public bool? BlockAtBudget { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

internal sealed class SettingsDb(DbContextOptions<SettingsDb> options) : DbContext(options)
{
    public const string Schema = "settings";

    public DbSet<SettingsRow> Settings => Set<SettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SettingsRow>(e =>
        {
            e.ToTable("Instance");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.ExcludedFiles).HasMaxLength(6000);
            e.Property(s => s.UpdatedBy).HasMaxLength(100);
        });
    }
}
