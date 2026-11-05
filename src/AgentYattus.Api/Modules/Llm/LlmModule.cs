using AgentYattus.Contracts;
using Anthropic;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>Un client de modèle prêt à servir, et le modèle qu'il utilise.</summary>
public sealed record LlmClient(IChatClient Client, string Model);

/// <summary>Ce que le module montre aux agents : le client du modèle choisi, sans jamais exposer la clé.</summary>
public interface ILlmClientProvider
{
    /// <returns>Null si aucune clé n'est disponible.</returns>
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
        services.AddScoped<ILlmClientProvider, LlmClientProvider>();
        return services.AddModuleDb<LlmDb>(LlmDb.Schema);
    }

    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder app)
    {
        var llm = app.MapGroup(LlmProtocol.Path).RequireAuthorization(Authentication.LlmAdminPolicy);
        llm.MapGet("/", GetAsync);
        llm.MapPost("/credentials", AddCredentialAsync);
        llm.MapDelete("/credentials/{id:guid}", RevokeAsync);
        llm.MapPut("/settings", UpdateSettingsAsync);
        return app;
    }

    private static async Task<Ok<LlmStatusDto>> GetAsync(LlmDb db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        // Identifiants v7 : trier par Id, c'est trier par date de création (SQLite ne trie pas un DateTimeOffset).
        var credentials = await db.Credentials.OrderByDescending(c => c.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(new LlmStatusDto(
            LlmCatalog.Providers,
            [.. credentials.Select(c => c.ToDto(settings?.ActiveCredentialId))],
            settings?.ActiveCredentialId,
            settings?.Model ?? LlmCatalog.DefaultModel(configuration)));
    }

    private static async Task<Results<Created<LlmCredentialDto>, ValidationProblem>> AddCredentialAsync(
        AddLlmCredentialRequest request, LlmDb db, IDataProtectionProvider protection, HttpContext http,
        ILogger<LlmDb> logger, CancellationToken cancellationToken)
    {
        var apiKey = request.ApiKey?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (LlmCatalog.Find(request.Provider) is null)
            errors["provider"] = ["Fournisseur inconnu."];
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 60)
            errors["label"] = ["Le libellé est obligatoire et fait au plus 60 caractères."];
        if (apiKey.Length is < 16 or > 512 || apiKey.Any(char.IsWhiteSpace))
            errors["apiKey"] = ["La clé doit faire entre 16 et 512 caractères, sans espace."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var credential = new LlmCredential
        {
            Id = Guid.CreateVersion7(),
            Provider = request.Provider,
            Label = request.Label.Trim(),
            // Seule la version chiffrée est enregistrée.
            ProtectedKey = LlmCredential.Protector(protection).Protect(apiKey),
            Last4 = apiKey[^4..],
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = http.User.Identity?.Name ?? "?",
        };
        db.Credentials.Add(credential);

        // La première clé devient la clé active : l'agent peut travailler tout de suite.
        var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
        settings.ActiveCredentialId ??= credential.Id;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clé « {Label} » (…{Last4}) ajoutée par {User}", credential.Label, credential.Last4, credential.CreatedBy);
        return TypedResults.Created($"{LlmProtocol.Credentials}/{credential.Id}", credential.ToDto(settings.ActiveCredentialId));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, LlmDb db, HttpContext http, ILogger<LlmDb> logger, CancellationToken cancellationToken)
    {
        if (await db.Credentials.FindAsync([id], cancellationToken) is not { } credential)
            return TypedResults.NotFound();
        if (credential.RevokedAt is null)
        {
            // Révoquer, c'est effacer la clé chiffrée : même avec le trousseau, plus rien à déchiffrer.
            credential.ProtectedKey = null;
            credential.RevokedAt = DateTimeOffset.UtcNow;
            credential.RevokedBy = http.User.Identity?.Name ?? "?";
            var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
            if (settings.ActiveCredentialId == id)
                settings.ActiveCredentialId = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Clé « {Label} » (…{Last4}) révoquée par {User}", credential.Label, credential.Last4, credential.RevokedBy);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> UpdateSettingsAsync(
        UpdateLlmSettingsRequest request, LlmDb db, HttpContext http, CancellationToken cancellationToken)
    {
        string provider = LlmCatalog.Anthropic.Id;
        if (request.CredentialId is { } credentialId)
        {
            var credential = await db.Credentials.FindAsync([credentialId], cancellationToken);
            if (credential is null || credential.RevokedAt is not null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["credentialId"] = ["Clé inconnue ou révoquée."] });
            provider = credential.Provider;
        }

        if (LlmCatalog.Find(provider)?.Models.Any(m => m.Id == request.Model) != true)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = ["Modèle inconnu pour ce fournisseur."] });

        var settings = await LlmSettings.GetOrCreateAsync(db, cancellationToken);
        settings.ActiveCredentialId = request.CredentialId;
        settings.Model = request.Model;
        settings.UpdatedBy = http.User.Identity?.Name ?? "?";
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}

/// <summary>Les fournisseurs et modèles proposés, avec les prix publics relevés le 2026-11-05.</summary>
internal static class LlmCatalog
{
    // ponytail: Anthropic seulement, seul fournisseur branché ; ajouter OpenAI ou Ollama avec leur adaptateur IChatClient.
    public static readonly LlmProviderDto Anthropic = new("anthropic", "Anthropic",
    [
        new("claude-haiku-5-5", "Claude Haiku 5.5", 0.10m, 0.50m),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5", 2m, 10m),
        new("claude-opus-5-5", "Claude Opus 5.5", 4m, 20m),
    ]);

    public static IReadOnlyList<LlmProviderDto> Providers { get; } = [Anthropic];

    public static LlmProviderDto? Find(string? id) => Providers.FirstOrDefault(p => p.Id == id);

    public static string DefaultModel(IConfiguration configuration) => configuration["Agents:Model"] ?? Anthropic.Models[0].Id;
}

/// <summary>
/// Le client du modèle : la clé active choisie dans l'application, sinon la clé de la configuration
/// (user-secrets, pratique en développement), sinon rien.
/// </summary>
internal sealed class LlmClientProvider(LlmDb db, IDataProtectionProvider protection, IConfiguration configuration) : ILlmClientProvider
{
    public async Task<LlmClient?> GetAsync(CancellationToken cancellationToken)
    {
        var model = await GetModelAsync(cancellationToken);
        var settings = await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken);
        string? apiKey = null;
        if (settings?.ActiveCredentialId is { } id
            && await db.Credentials.FindAsync([id], cancellationToken) is { ProtectedKey: { } protectedKey })
            apiKey = LlmCredential.Protector(protection).Unprotect(protectedKey);
        apiKey ??= configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;
        return new LlmClient(new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000), model);
    }

    public async Task<string> GetModelAsync(CancellationToken cancellationToken) =>
        (await db.Settings.FindAsync([LlmSettings.SingletonId], cancellationToken))?.Model ?? LlmCatalog.DefaultModel(configuration);
}

internal sealed class LlmCredential
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string Label { get; set; }

    /// <summary>La clé chiffrée par Data Protection ; null une fois révoquée.</summary>
    public string? ProtectedKey { get; set; }
    public required string Last4 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }

    // Le « purpose » isole ces données : une donnée protégée pour un autre usage ne se déchiffre pas ici.
    public static IDataProtector Protector(IDataProtectionProvider protection) => protection.CreateProtector("AgentYattus.Llm.ApiKey.v1");

    public LlmCredentialDto ToDto(Guid? activeId) =>
        new(Id, Provider, Label, Last4, CreatedAt, CreatedBy, RevokedAt, IsActive: Id == activeId);
}

internal sealed class LlmSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public Guid? ActiveCredentialId { get; set; }
    public required string Model { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static async Task<LlmSettings> GetOrCreateAsync(LlmDb db, CancellationToken cancellationToken)
    {
        if (await db.Settings.FindAsync([SingletonId], cancellationToken) is { } settings)
            return settings;
        settings = new LlmSettings { Model = LlmCatalog.Anthropic.Models[0].Id, UpdatedAt = DateTimeOffset.UtcNow };
        db.Settings.Add(settings);
        return settings;
    }
}

internal sealed class LlmDb(DbContextOptions<LlmDb> options) : DbContext(options)
{
    public const string Schema = "llm";

    public DbSet<LlmCredential> Credentials => Set<LlmCredential>();
    public DbSet<LlmSettings> Settings => Set<LlmSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<LlmSettings>().Property(s => s.Id).ValueGeneratedNever();
    }
}
