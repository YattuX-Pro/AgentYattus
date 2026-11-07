using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>Un client de modèle prêt à servir, et le modèle qu'il utilise (« fournisseur/modèle »).</summary>
public sealed record LlmClient(IChatClient Client, string Model);

/// <summary>Le prix d'un modèle (« fournisseur/modèle »), au catalogue models.dev (ADR 0010). Inconnu ou gratuit : 0.</summary>
public interface ILlmPricing
{
    /// <summary>Le coût estimé, en micro-dollars (un millionième de dollar).</summary>
    long CostMicros(string model, long inputTokens, long outputTokens);

    /// <summary>Vrai si le catalogue donne un prix non nul à ce modèle.</summary>
    bool IsPaid(string model);
}

/// <summary>Ce que le module montre aux agents : le client du modèle choisi pour un rôle, sans jamais exposer la clé.</summary>
public interface ILlmClientProvider
{
    /// <param name="role">La clé du rôle d'agent (« analyste ») : il peut avoir son propre modèle.</param>
    /// <returns>Null si aucun modèle n'est utilisable.</returns>
    Task<LlmClient?> GetAsync(string role, CancellationToken cancellationToken);

    Task<string> GetModelAsync(string role, CancellationToken cancellationToken);
}

/// <summary>Le modèle propre à un rôle d'agent, à la place du modèle par défaut. Le module Agents connaît les rôles, pas celui-ci.</summary>
public interface ILlmAgentModels
{
    Task<LlmSelectionDto?> GetAsync(string role, CancellationToken cancellationToken);

    /// <param name="selection">Null pour revenir au modèle par défaut.</param>
    /// <returns>Null si tout va bien, sinon la raison du refus.</returns>
    Task<string?> SetAsync(string role, LlmSelectionDto? selection, string user, CancellationToken cancellationToken);
}

public static class LlmModule
{
    public static IServiceCollection AddLlmModule(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("AgentYattus");
        // Le trousseau de chiffrement vit hors de la base : voler la base seule ne suffit pas à lire les clés.
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        else if (LocalMode.IsOn(configuration))
            // Mode local : à côté de la base, dans le dossier de données de l'utilisateur (ADR 0014).
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(LocalMode.DataFolder(configuration), "keys")));
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
        services.AddSingleton<ILlmPricing, LlmPricing>();
        services.AddScoped<ILlmAgentModels, LlmAgentModels>();
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
        llm.MapPut("/free", UpdateFreeModeAsync);
        // Pour « Nouveau collègue » : n'importe quel utilisateur choisit le modèle de ses agents parmi ceux de l'instance.
        app.MapGet(LlmProtocol.Choices, GetModelsAsync);
        return app;
    }

    private static async Task<Ok<LlmStatusDto>> GetAsync(LlmDb db, ModelCatalog catalog, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        // Identifiants v7 : trier par Id, c'est trier par date de création (SQLite ne trie pas un DateTimeOffset).
        var connections = await db.Connections.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(new LlmStatusDto(
            [.. LlmProviders.All.Select(p => new LlmProviderDto(p.Id, p.Name, p.Kind, p.BaseUrl, catalog.ModelsOf(p.Id).Count, p.Notice))],
            [.. connections.Select(c => c.ToDto())],
            settings?.Default, settings?.FreeMode ?? false, settings?.Chain ?? []));
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
            settings.Chain = [.. settings.Chain.Where(c => c.ConnectionId != id)];
            // Les rôles qui utilisaient cette connexion reviennent au modèle par défaut.
            db.AgentModels.RemoveRange(await db.AgentModels.Where(a => a.ConnectionId == id).ToListAsync(cancellationToken));
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

    /// <summary>
    /// Le mode Gratuit. La chaîne n'accepte que des modèles gratuits (prix nul au catalogue, ou serveur local) :
    /// c'est la garantie qu'elle ne bascule jamais vers un modèle payant.
    /// </summary>
    private static async Task<Results<NoContent, ValidationProblem>> UpdateFreeModeAsync(
        UpdateFreeModeRequest request, LlmDb db, ModelCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        string? error = request.Chain.Count > 10 ? "La chaîne compte au plus 10 modèles."
            : request.Enabled && request.Chain.Count == 0 ? "Ajoute au moins un modèle gratuit à la chaîne."
            : null;
        foreach (var selection in request.Chain)
        {
            if (error is not null)
                break;
            error = await ValidateAsync(selection, db, catalog, cancellationToken);
            if (error is null && !await IsFreeAsync(selection, db, catalog, cancellationToken))
                error = $"{selection.Model} n'est pas gratuit : la chaîne n'accepte que des modèles gratuits.";
        }

        if (error is not null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["chain"] = [error] });

        var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
        settings.FreeMode = request.Enabled;
        settings.Chain = [.. request.Chain.DistinctBy(c => (c.ConnectionId, c.Model))];
        settings.UpdatedBy = http.User.Identity?.Name ?? "?";
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<bool> IsFreeAsync(LlmSelectionDto selection, LlmDb db, ModelCatalog catalog, CancellationToken cancellationToken)
    {
        var connection = (await db.Connections.FindAsync([selection.ConnectionId], cancellationToken))!;
        // Un serveur local tourne sur ta machine : il est gratuit. Une adresse personnalisée, on n'en sait rien.
        return LlmProviders.Find(connection.Provider)!.Kind == LlmConnectionKind.Local
            || catalog.Find(connection.Provider, selection.Model)?.Free == true;
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
/// Le client du modèle d'un rôle : son modèle propre, sinon le modèle par défaut choisi dans l'application,
/// sinon la clé Anthropic de la configuration (user-secrets, pratique en développement), sinon rien.
/// </summary>
internal sealed class LlmClientProvider(LlmDb db, IDataProtectionProvider protection, IConfiguration configuration) : ILlmClientProvider
{
    public async Task<LlmClient?> GetAsync(string role, CancellationToken cancellationToken)
    {
        if (await FreeChainAsync(role, cancellationToken) is { Count: > 0 } chain)
        {
            var links = chain.Select(link => Create(link.Connection, link.Model)).ToList();
            return new LlmClient(new FallbackChatClient(links), links[0].Model);
        }

        if (await ResolveAsync(role, cancellationToken) is var (connection, model) && connection is not null)
            return Create(connection, model);

        if (configuration["Anthropic:ApiKey"] is not { Length: > 0 } apiKey)
            return null;
        var configured = ConfiguredModel;
        return new LlmClient(LlmProviders.CreateClient(LlmProviders.Anthropic, apiKey, "", configured), $"anthropic/{configured}");
    }

    public async Task<string> GetModelAsync(string role, CancellationToken cancellationToken)
    {
        if (await FreeChainAsync(role, cancellationToken) is { Count: > 0 } chain)
            return $"gratuit : {chain[0].Connection.Provider}/{chain[0].Model}" + (chain.Count > 1 ? $" (+{chain.Count - 1})" : "");
        return await ResolveAsync(role, cancellationToken) is ({ } connection, var model) ? $"{connection.Provider}/{model}" : $"anthropic/{ConfiguredModel}";
    }

    private LlmClient Create(LlmConnection connection, string model)
    {
        var provider = LlmProviders.Find(connection.Provider)!;
        return new LlmClient(
            LlmProviders.CreateClient(provider, connection.ApiKey(protection), connection.BaseUrlOf(provider), model),
            $"{provider.Id}/{model}");
    }

    /// <summary>
    /// La chaîne gratuite, si le mode Gratuit est actif et que le rôle n'a pas son propre modèle
    /// (un modèle choisi exprès pour un rôle l'emporte). Les connexions révoquées sont sautées.
    /// </summary>
    private async Task<List<(LlmConnection Connection, string Model)>> FreeChainAsync(string role, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        if (settings is not { FreeMode: true } || await db.AgentModels.FindAsync([role], cancellationToken) is not null)
            return [];
        var chain = new List<(LlmConnection, string)>();
        foreach (var link in settings.Chain)
            if (await db.Connections.FindAsync([link.ConnectionId], cancellationToken) is { RevokedAt: null } connection)
                chain.Add((connection, link.Model));
        return chain;
    }

    private string ConfiguredModel => configuration["Agents:Model"] ?? "claude-haiku-5-5";

    private async Task<(LlmConnection? Connection, string Model)> ResolveAsync(string role, CancellationToken cancellationToken)
    {
        if (await db.AgentModels.FindAsync([role], cancellationToken) is { } own
            && await db.Connections.FindAsync([own.ConnectionId], cancellationToken) is { RevokedAt: null } ownConnection)
            return (ownConnection, own.Model);
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        if (settings?.DefaultConnectionId is { } id && await db.Connections.FindAsync([id], cancellationToken) is { RevokedAt: null } connection)
            return (connection, settings.Model);
        return (null, "");
    }
}

internal sealed class LlmAgentModels(LlmDb db, ModelCatalog catalog, ILogger<LlmAgentModels> logger) : ILlmAgentModels
{
    public async Task<LlmSelectionDto?> GetAsync(string role, CancellationToken cancellationToken) =>
        await db.AgentModels.FindAsync([role], cancellationToken) is { } own ? new LlmSelectionDto(own.ConnectionId, own.Model) : null;

    public async Task<string?> SetAsync(string role, LlmSelectionDto? selection, string user, CancellationToken cancellationToken)
    {
        if (selection is not null && await LlmModule.ValidateAsync(selection, db, catalog, cancellationToken) is { } error)
            return error;
        var own = await db.AgentModels.FindAsync([role], cancellationToken);
        if (own is not null)
            db.AgentModels.Remove(own);
        if (selection is not null)
            db.AgentModels.Add(new LlmAgentModel { Role = role, ConnectionId = selection.ConnectionId, Model = selection.Model });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Modèle du rôle {Role} : {Model} (par {User})", role, selection?.Model ?? "par défaut", user);
        return null;
    }
}

/// <summary>Le modèle propre à un rôle d'agent.</summary>
internal sealed class LlmAgentModel
{
    [System.ComponentModel.DataAnnotations.Key]
    [System.ComponentModel.DataAnnotations.MaxLength(40)]
    public required string Role { get; set; }
    public Guid ConnectionId { get; set; }
    public required string Model { get; set; }
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

    public bool FreeMode { get; set; }

    /// <summary>La chaîne gratuite, rangée en JSON : une petite liste ordonnée, lue et écrite d'un bloc.</summary>
    public string FreeChain { get; set; } = "[]";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<LlmSelectionDto> Chain
    {
        get => string.IsNullOrEmpty(FreeChain) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<LlmSelectionDto>>(FreeChain) ?? [];
        set => FreeChain = System.Text.Json.JsonSerializer.Serialize(value);
    }

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
    public DbSet<LlmAgentModel> AgentModels => Set<LlmAgentModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // Les noms en base restent ceux de la leçon 32 : renommer une table coûte une migration pour rien.
        modelBuilder.Entity<LlmConnection>().ToTable("Credentials");
        modelBuilder.Entity<LlmSettings>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<LlmSettings>().Property(s => s.DefaultConnectionId).HasColumnName("ActiveCredentialId");
    }
}

internal sealed class LlmPricing(ModelCatalog catalog) : ILlmPricing
{
    // Le prix du catalogue est en dollars par million de tokens : tokens × prix = des micro-dollars.
    public long CostMicros(string model, long inputTokens, long outputTokens) =>
        Find(model) is { InputPrice: { } input, OutputPrice: { } output }
            ? (long)Math.Round(inputTokens * input + outputTokens * output, MidpointRounding.AwayFromZero)
            : 0;

    public bool IsPaid(string model) => Find(model) is { } m && (m.InputPrice > 0 || m.OutputPrice > 0);

    private CatalogModel? Find(string model) =>
        model.IndexOf('/') is > 0 and var slash ? catalog.Find(model[..slash], model[(slash + 1)..]) : null;
}
