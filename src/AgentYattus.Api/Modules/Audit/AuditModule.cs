using AgentYattus.Api.Modules.Settings;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services
            .AddModuleDb<AuditDb>(AuditDb.Schema)
            .AddScoped(sp => new AuditLog(sp.GetRequiredService<AuditDb>()))
            .AddHostedService<AuditRetention>();

    private const int MaxFeed = 500;

    /// <summary>
    /// Le journal de toutes les missions, les plus récents d'abord. Réservé aux administrateurs :
    /// il révèle ce que font toutes les missions de l'instance.
    /// </summary>
    // ponytail: une seule page ; ajouter un curseur (?before=id) quand 500 lignes ne suffiront plus.
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AuditProtocol.Path, async Task<Results<Ok<AuditFeedEntryDto[]>, ValidationProblem>> (
            AuditDb db, CancellationToken cancellationToken, int limit = 200) =>
        {
            if (limit is < 1 or > MaxFeed)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = [$"La limite est comprise entre 1 et {MaxFeed}."],
                });
            return TypedResults.Ok(await db.Events.OrderByDescending(e => e.Id).Take(limit)
                .Select(e => new AuditFeedEntryDto(e.MissionId, e.Type, e.At)).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        // Export CSV, pour un tableur ou un archivage. Borné, et protégé contre l'injection de formules.
        app.MapGet(AuditProtocol.Export, async Task<Results<FileContentHttpResult, ValidationProblem>> (
            AuditDb db, CancellationToken ct, int limit = 5000) =>
        {
            if (limit is < 1 or > MaxExport)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = [$"La limite est comprise entre 1 et {MaxExport}."],
                });
            var rows = await db.Events.OrderByDescending(e => e.Id).Take(limit).ToListAsync(ct);
            var csv = new System.Text.StringBuilder("horodatage,mission,evenement\r\n");
            foreach (var e in rows)
                csv.Append(Csv(e.At.ToString("O"))).Append(',').Append(Csv(e.MissionId.ToString())).Append(',').Append(Csv(e.Type)).Append("\r\n");
            // L'en-tête UTF-8 (BOM) fait reconnaître les accents à Excel.
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return TypedResults.File(bytes, "text/csv; charset=utf-8", $"audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }).RequireAuthorization(Authentication.LlmAdminPolicy);
        return app;
    }

    private const int MaxExport = 50_000;

    /// <summary>
    /// Une cellule CSV sûre. Un tableur exécute une cellule qui commence par = + - @ (ou une tabulation, un retour) :
    /// un type d'événement venu d'un nom d'outil ou d'un modèle pourrait devenir une formule. On la préfixe d'une apostrophe.
    /// </summary>
    internal static string Csv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

public sealed class AuditLog
{
    private readonly AuditDb _db;

    internal AuditLog(AuditDb db) => _db = db;

    public async Task AppendAsync(Guid missionId, string type, CancellationToken cancellationToken)
    {
        var e = new AuditEvent { MissionId = missionId, Type = type, At = DateTimeOffset.UtcNow };
        _db.Events.Add(e);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Un événement refusé ne doit pas rester suivi : sinon chaque ajout suivant le renverrait et échouerait
            // à son tour, y compris le statut final de la mission.
            _db.Entry(e).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            throw;
        }
    }

    /// <summary>
    /// Efface les événements antérieurs à <paramref name="cutoff"/>. Les identifiants croissent avec le temps :
    /// on cherche le dernier événement trop ancien, puis on efface tout ce qui le précède en une requête.
    /// </summary>
    // On lit les dates une à une, du plus ancien au plus récent, et on s'arrête au premier assez récent :
    // SQLite ne sait pas comparer des DateTimeOffset dans une requête.
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        long? last = null;
        await foreach (var e in _db.Events.OrderBy(e => e.Id).Select(e => new { e.Id, e.At }).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (e.At >= cutoff)
                break;
            last = e.Id;
        }
        return last is { } id ? await _db.Events.Where(e => e.Id <= id).ExecuteDeleteAsync(cancellationToken) : 0;
    }

    /// <summary>Les types des événements qui commencent par ce préfixe, toutes missions confondues (statistiques).</summary>
    // ponytail: lit tous les événements « usage: » ; une table de statistiques agrégées si le journal devient très gros.
    public async Task<List<string>> TypesStartingWithAsync(string prefix, CancellationToken cancellationToken) =>
        await _db.Events.Where(e => e.Type.StartsWith(prefix)).Select(e => e.Type).ToListAsync(cancellationToken);

    /// <summary>Le journal d'une mission, dans l'ordre où les événements ont été inscrits.</summary>
    public async Task<AuditEntryDto[]> ReadAsync(Guid missionId, CancellationToken cancellationToken) =>
        await _db.Events.Where(e => e.MissionId == missionId).OrderBy(e => e.Id)
            .Select(e => new AuditEntryDto(e.Type, e.At)).ToArrayAsync(cancellationToken);
}

/// <summary>Chaque jour, efface les événements plus anciens que la rétention choisie (0 : tout garder). ADR 0010.</summary>
internal sealed class AuditRetention(IServiceScopeFactory scopes, ILogger<AuditRetention> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var days = (await scope.ServiceProvider.GetRequiredService<IInstanceSettings>().GetAsync(stoppingToken)).AuditRetentionDays;
                if (days > 0)
                {
                    var removed = await scope.ServiceProvider.GetRequiredService<AuditLog>()
                        .PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-days), stoppingToken);
                    if (removed > 0)
                        logger.LogInformation("Audit : {Count} événements de plus de {Days} jours effacés", removed, days);
                }
            }
            // Une purge ratée (base indisponible au démarrage…) ne doit pas arrêter l'hôte : on réessaie demain.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Purge de l'audit ignorée");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

internal sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid MissionId { get; set; }
    public required string Type { get; set; }
    public DateTimeOffset At { get; set; }
}

internal sealed class AuditDb(DbContextOptions<AuditDb> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditEvent>().HasIndex(e => e.MissionId);
    }
}
