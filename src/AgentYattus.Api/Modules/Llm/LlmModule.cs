using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>Un client de modèle prêt à servir, et le modèle qu'il utilise (« fournisseur/modèle »).</summary>
public sealed record LlmClient(IChatClient Client, string Model);

/// <summary>Ce que le module montre aux agents : le client du modèle choisi, sans jamais exposer la clé.</summary>
public interface ILlmClientProvider
{
    /// <returns>Null si aucun modèle n'est utilisable.</returns>
    Task<LlmClient?> GetAsync(CancellationToken cancellationToken);

    Task<string> GetModelAsync(CancellationToken cancellationToken);
}

public static class LlmModule
{
    public static IServiceCollection AddLlmModule(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("AgentYattus");
        // Le trousseau de chiffrement vit hors de la base : voler la base seule ne suffit pas à lire les clés.
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddHttpClient(ModelCatalog.HttpClientName, http =>
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            // models.dev refuse les requêtes sans User-Agent explicite.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AgentYattus");
        })
            // Pas de redirection, et l'adresse contactée est vérifiée après la résolution DNS (voir LlmProviders).
            .ConfigurePrimaryHttpMessageHandler(LlmProviders.CreateGuardedHandler);
        services.AddSingleton<ModelCatalog>();
        services.AddHostedService<CatalogRefresher>();
        services.AddScoped<ILlmClientProvider, LlmClientProvider>();
        return services.AddModuleDb<LlmDb>(LlmDb.Schema);
    }

    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder app)
    {
        var llm = app.MapGroup(LlmProtocol.Path).RequireAuthorization(Authentication.LlmAdminPolicy);
        llm.MapGet("/", GetAsync);
        llm.MapGet("/models", GetModelsAsync);
        llm.MapPost("/connections", ConnectAsync);
        llm.MapDelete("/connections/{id:guid}", RevokeAsync);
        llm.MapPut("/settings", UpdateSettingsAsync);
        return app;
    }

    private static async Task<Ok<LlmStatusDto>> GetAsync(LlmDb db, ModelCatalog catalog, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        // Identifiants v7 : trier par Id, c'est trier par date de création (SQLite ne trie pas un DateTimeOffset).
        var connections = await db.Connections.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(new LlmStatusDto(
            [.. LlmProviders.All.Select(p => new LlmProviderDto(p.Id, p.Name, p.Kind, p.BaseUrl, catalog.ModelsOf(p.Id).Count))],
            [.. connections.Select(c => c.ToDto())],
            settings?.Default));
    }

    /// <summary>Les modèles de chaque fournisseur connecté : le catalogue, ou ce que le serveur local annonce.</summary>
    private static async Task<Ok<LlmModelsDto>> GetModelsAsync(
        LlmDb db, ModelCatalog catalog, IDataProtectionProvider protection, IHttpClientFactory http, CancellationToken cancellationToken)
    {
        var connections = await db.Connections.Where(c => c.RevokedAt == null).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var lists = await Task.WhenAll(connections.Select(async connection =>
        {
            var provider = LlmProviders.Find(connection.Provider)!;
            try
            {
                var models = provider.Kind == LlmConnectionKind.ApiKey
                    ? catalog.ModelsOf(provider.Id)
                    : await ModelCatalog.DiscoverAsync(http.CreateClient(ModelCatalog.HttpClientName), connection.BaseUrlOf(provider),
                        connection.ApiKey(protection), cancellationToken);
                return (Models: models.Select(m => m.ToDto(connection)), Error: (LlmConnectionErrorDto?)null);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return ([], new LlmConnectionErrorDto(connection.Id, $"{connection.Label} : serveur injoignable ou réponse invalide."));
            }
        }));
        return TypedResults.Ok(new LlmModelsDto(
            [.. lists.SelectMany(l => l.Models)], [.. lists.Select(l => l.Error).OfType<LlmConnectionErrorDto>()]));
    }

    private static async Task<Results<Created<LlmConnectionDto>, ValidationProblem>> ConnectAsync(
        AddLlmConnectionRequest request, LlmDb db, IDataProtectionProvider protection, HttpContext http,
        ILogger<LlmDb> logger, CancellationToken cancellationToken)
    {
        var apiKey = request.ApiKey?.Trim() ?? "";
        var baseUrl = request.BaseUrl?.Trim();
        var provider = LlmProviders.Find(request.Provider);
        var errors = new Dictionary<string, string[]>();
        if (provider is null)
            errors["provider"] = ["Fournisseur inconnu."];
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 60)
            errors["label"] = ["Le libellé est obligatoire et fait au plus 60 caractères."];
        // Une clé est obligatoire chez un fournisseur en ligne ; facultative sur un serveur local ou personnalisé.
        if ((provider?.Kind == LlmConnectionKind.ApiKey || apiKey.Length > 0) && (apiKey.Length is < 16 or > 512 || apiKey.Any(char.IsWhiteSpace)))
            errors["apiKey"] = ["La clé doit faire entre 16 et 512 caractères, sans espace."];
        if (provider?.Kind == LlmConnectionKind.ApiKey)
            // L'adresse d'un fournisseur en ligne est imposée : on n'envoie pas sa clé ailleurs.
            baseUrl = null;
        else if (provider is not null && LlmProviders.ValidateBaseUrl(baseUrl ?? provider.BaseUrl) is { } urlError)
            errors["baseUrl"] = [urlError];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var connection = new LlmConnection
        {
            Id = Guid.CreateVersion7(),
            Provider = provider!.Id,
            Label = request.Label.Trim(),
            // Seule la version chiffrée est enregistrée.
            ProtectedKey = apiKey.Length > 0 ? LlmConnection.Protector(protection).Protect(apiKey) : null,
            Last4 = apiKey.Length > 0 ? apiKey[^4..] : "",
            BaseUrl = provider.Kind == LlmConnectionKind.ApiKey ? null : baseUrl ?? provider.BaseUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = http.User.Identity?.Name ?? "?",
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Fournisseur {Provider} « {Label} » connecté par {User}", connection.Provider, connection.Label, connection.CreatedBy);
        return TypedResults.Created($"{LlmProtocol.Connections}/{connection.Id}", connection.ToDto());
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, LlmDb db, HttpContext http, ILogger<LlmDb> logger, CancellationToken cancellationToken)
    {
        if (await db.Connections.FindAsync([id], cancellationToken) is not { } connection)
            return TypedResults.NotFound();
        if (connection.RevokedAt is null)
        {
            // Révoquer, c'est effacer la clé chiffrée : même avec le trousseau, plus rien à déchiffrer.
            connection.ProtectedKey = null;
            connection.RevokedAt = DateTimeOffset.UtcNow;
            connection.RevokedBy = http.User.Identity?.Name ?? "?";
            var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
            if (settings.DefaultConnectionId == id)
                settings.DefaultConnectionId = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Fournisseur « {Label} » révoqué par {User}", connection.Label, connection.RevokedBy);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> UpdateSettingsAsync(
        UpdateLlmSettingsRequest request, LlmDb db, ModelCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(request.Default, db, catalog, cancellationToken) is { } error)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["default"] = [error] });

        var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
        settings.DefaultConnectionId = request.Default.ConnectionId;
        settings.Model = request.Default.Model;
        settings.UpdatedBy = http.User.Identity?.Name ?? "?";
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Un choix est valable si la connexion existe, n'est pas révoquée, et propose ce modèle.</summary>
    internal static async Task<string?> ValidateAsync(LlmSelectionDto selection, LlmDb db, ModelCatalog catalog, CancellationToken cancellationToken)
    {
        var connection = await db.Connections.FindAsync([selection.ConnectionId], cancellationToken);
        if (connection is null || connection.RevokedAt is not null)
            return "Fournisseur inconnu ou révoqué.";
        // Les modèles d'un serveur local changent sans prévenir : on vérifie seulement la forme de l'identifiant.
        var known = LlmProviders.Find(connection.Provider)!.Kind == LlmConnectionKind.ApiKey
            ? catalog.Find(connection.Provider, selection.Model) is not null
            : ModelCatalog.IsValidId(selection.Model);
        return known ? null : "Modèle inconnu pour ce fournisseur.";
    }
}

/// <summary>
/// Le client du modèle : le modèle par défaut choisi dans l'application, sinon la clé Anthropic de la configuration
/// (user-secrets, pratique en développement), sinon rien.
/// </summary>
internal sealed class LlmClientProvider(LlmDb db, IDataProtectionProvider protection, IConfiguration configuration) : ILlmClientProvider
{
    public async Task<LlmClient?> GetAsync(CancellationToken cancellationToken)
    {
        if (await ResolveAsync(cancellationToken) is var (connection, model) && connection is not null)
        {
            var provider = LlmProviders.Find(connection.Provider)!;
            return new LlmClient(
                LlmProviders.CreateClient(provider, connection.ApiKey(protection), connection.BaseUrlOf(provider), model),
                $"{provider.Id}/{model}");
        }

        if (configuration["Anthropic:ApiKey"] is not { Length: > 0 } apiKey)
            return null;
        var configured = ConfiguredModel;
        return new LlmClient(LlmProviders.CreateClient(LlmProviders.Anthropic, apiKey, "", configured), $"anthropic/{configured}");
    }

    public async Task<string> GetModelAsync(CancellationToken cancellationToken) =>
        await ResolveAsync(cancellationToken) is ({ } connection, var model) ? $"{connection.Provider}/{model}" : $"anthropic/{ConfiguredModel}";

    private string ConfiguredModel => configuration["Agents:Model"] ?? "claude-haiku-5-5";

    private async Task<(LlmConnection? Connection, string Model)> ResolveAsync(CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        if (settings?.DefaultConnectionId is { } id && await db.Connections.FindAsync([id], cancellationToken) is { RevokedAt: null } connection)
            return (connection, settings.Model);
        return (null, "");
    }
}

/// <summary>Un fournisseur connecté (table historique « Credentials »).</summary>
internal sealed class LlmConnection
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string Label { get; set; }

    /// <summary>La clé chiffrée par Data Protection ; null sans clé, ou une fois révoquée.</summary>
    public string? ProtectedKey { get; set; }

    /// <summary>Les 4 derniers caractères de la clé, ou vide sans clé.</summary>
    public required string Last4 { get; set; }

    /// <summary>L'adresse d'un serveur local ou personnalisé ; null pour un fournisseur en ligne.</summary>
    public string? BaseUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }

    // Le « purpose » isole ces données : une donnée protégée pour un autre usage ne se déchiffre pas ici.
    public static IDataProtector Protector(IDataProtectionProvider protection) => protection.CreateProtector("AgentYattus.Llm.ApiKey.v1");

    public string? ApiKey(IDataProtectionProvider protection) => ProtectedKey is null ? null : Protector(protection).Unprotect(ProtectedKey);

    public string BaseUrlOf(LlmProvider provider) => BaseUrl ?? provider.BaseUrl ?? "";

    public LlmConnectionDto ToDto() => new(Id, Provider, LlmProviders.Find(Provider)?.Name ?? Provider, Label,
        Last4.Length > 0 ? Last4 : null, BaseUrl, CreatedAt, CreatedBy, RevokedAt);
}

internal static class CatalogModelExtensions
{
    public static LlmModelDto ToDto(this CatalogModel m, LlmConnection connection) =>
        new(connection.Id, connection.Provider, m.Id, m.Name, m.ContextWindow, m.InputPrice, m.OutputPrice, m.Reasoning, m.Free);
}

internal sealed class LlmSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>La connexion du modèle par défaut (colonne historique « ActiveCredentialId »).</summary>
    public Guid? DefaultConnectionId { get; set; }
    public required string Model { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LlmSelectionDto? Default => DefaultConnectionId is { } id ? new LlmSelectionDto(id, Model) : null;

    public static async Task<LlmSettings> GetOrCreateAsync(LlmDb db, CancellationToken cancellationToken)
    {
        if (await db.Settings.FindAsync([SingletonId], cancellationToken) is { } settings)
            return settings;
        settings = new LlmSettings { Model = "", UpdatedAt = DateTimeOffset.UtcNow };
        db.Settings.Add(settings);
        return settings;
    }
}

internal sealed class LlmDb(DbContextOptions<LlmDb> options) : DbContext(options)
{
    public const string Schema = "llm";

    public DbSet<LlmConnection> Connections => Set<LlmConnection>();
    public DbSet<LlmSettings> Settings => Set<LlmSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // Les noms en base restent ceux de la leçon 32 : renommer une table coûte une migration pour rien.
        modelBuilder.Entity<LlmConnection>().ToTable("Credentials");
        modelBuilder.Entity<LlmSettings>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<LlmSettings>().Property(s => s.DefaultConnectionId).HasColumnName("ActiveCredentialId");
    }
}
