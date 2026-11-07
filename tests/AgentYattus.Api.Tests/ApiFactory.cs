using System.Collections.Concurrent;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Contracts;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentYattus.Api.Tests;

// SQLite sur fichier temporaire à la place de PostgreSQL : les tests tournent sans Docker.
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agentyattus-{Guid.NewGuid():N}.db");

    public FakeAgentRunner Runner { get; } = new();

    /// <summary>Des réglages en plus, à donner avant le premier client (« Projects:AllowedRoots:0 »…).</summary>
    public Dictionary<string, string> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
        ConfigureBase(builder);
    }

    private void ConfigureBase(IWebHostBuilder builder) =>
        // Pas d'appel à models.dev pendant les tests : le catalogue embarqué suffit.
        builder.UseSetting("Llm:CatalogUrl", "")
            // Chaque test part d'une galerie vide : il ajoute les portraits dont il a besoin.
            .UseSetting("Team:SeedFolder", "")
            .ConfigureServices(services =>
        {
            UseSqlite<MissionsDb>(services);
            UseSqlite<AuditDb>(services);
            UseSqlite<LlmDb>(services);
            UseSqlite<IdentityDb>(services);
            UseSqlite<ProjectsDb>(services);
            UseSqlite<SettingsDb>(services);
            UseSqlite<AgentsDb>(services);
            UseSqlite<AgentYattus.Api.Modules.Team.TeamDb>(services);
            // Jamais d'appel au vrai modèle pendant les tests, même si une clé est dans les user-secrets.
            services.RemoveAll<ILlmClientProvider>();
            services.AddSingleton<ILlmClientProvider>(new FixedLlmClientProvider(null));
            ConfigureAgents(services);
            ConfigureAuthentication(services);
        });

    // Par défaut, un utilisateur de test connecté avec tous les rôles : les tests des missions ne parlent pas de jetons.
    // AuthTests remplace ce comportement pour vérifier la vraie validation des jetons.
    protected virtual void ConfigureAuthentication(IServiceCollection services) =>
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
            .Services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

    // Par défaut, un faux exécuteur : les tests des missions ne font pas travailler d'agent.
    protected virtual void ConfigureAgents(IServiceCollection services)
    {
        services.RemoveAll<IAgentRunner>();
        services.AddSingleton<IAgentRunner>(Runner);
    }

    private void UseSqlite<TDb>(IServiceCollection services) where TDb : DbContext
    {
        services.RemoveAll<IDbContextOptionsConfiguration<TDb>>();
        services.AddDbContext<TDb>(o => o.UseSqlite($"Data Source={_dbPath}"));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}

public sealed class TestAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    /// <summary>En-tête de test pour choisir les rôles de l'utilisateur (par défaut : llm-admin).</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>En-tête de test pour choisir l'utilisateur (par défaut : « test ») : son « sub » et son nom.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>En-tête de test pour une requête sans connexion (images publiques, par exemple).</summary>
    public const string AnonymousHeader = "X-Test-Anonymous";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(AnonymousHeader))
            return Task.FromResult(AuthenticateResult.NoResult());
        var roles = Request.Headers.TryGetValue(RolesHeader, out var header) ? header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries) : ["llm-admin"];
        var user = Request.Headers.TryGetValue(UserHeader, out var u) ? u.ToString() : "test";
        var identity = new ClaimsIdentity(
            [new Claim("sub", user), new Claim("preferred_username", user), .. roles.Select(r => new Claim(ClaimTypes.Role, r))],
            SchemeName, "preferred_username", ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class FakeAgentRunner : IAgentRunner
{
    public const string TimeoutGoal = "simule un délai HTTP dépassé";
    public const string LongGoal = "mission longue";

    /// <summary>Une mission qui réussit et coûte 0,0025 $ (2 500 micro-dollars).</summary>
    public const string PaidGoal = "mission payante";

    private static async Task<AgentOutcome> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Inatteignable.");
    }

    public ConcurrentQueue<string> Goals { get; } = new();

    /// <summary>Le dossier de travail reçu pour chaque but.</summary>
    public ConcurrentDictionary<string, string?> Workspaces { get; } = new();

    /// <summary>Les sièges reçus par mission : ce que les collègues ajoutent aux consignes (présentation, compétences).</summary>
    public ConcurrentDictionary<Guid, IReadOnlyDictionary<string, AgentSeat>?> Seats { get; } = new();

    /// <summary>Seulement les présentations, par mission et par rôle.</summary>
    public IReadOnlyDictionary<Guid, Dictionary<string, string>?> Styles =>
        Seats.ToDictionary(m => m.Key, m => m.Value?.ToDictionary(s => s.Key, s => s.Value.Style));

    /// <summary>L'agent choisi au lancement, par mission (ADR 0013).</summary>
    public ConcurrentDictionary<Guid, string?> Agents { get; } = new();

    public Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace,
        IReadOnlyDictionary<string, AgentSeat>? seats, string? agent, string? launchedBy, CancellationToken cancellationToken,
        Func<string, Task>? onText = null)
    {
        Goals.Enqueue(goal);
        Workspaces[goal] = workspace;
        Seats[missionId] = seats;
        Agents[missionId] = agent;
        // Une mission longue : elle tourne jusqu'à son annulation (arrêt, durée maximum, fin de l'hôte).
        if (goal.StartsWith(LongGoal, StringComparison.Ordinal))
            return WaitForCancellationAsync(cancellationToken);
        // Ce que lèvera un HttpClient en délai dépassé dans le vrai runner.
        return goal == TimeoutGoal
            ? Task.FromException<AgentOutcome>(new TaskCanceledException())
            : Task.FromResult(new AgentOutcome($"Réponse à : {goal}",
                Usage: goal.StartsWith(PaidGoal, StringComparison.Ordinal) ? new AgentUsage(100, 50, 2_500) : null));
    }
}

/// <summary>Un fournisseur de modèle figé : le faux modèle des tests, ou aucun (« pas de clé »).</summary>
public sealed class FixedLlmClientProvider(IChatClient? client, string model = "claude-haiku-5-5") : ILlmClientProvider
{
    public Task<LlmClient?> GetAsync(string role, CancellationToken cancellationToken) =>
        Task.FromResult(client is null ? null : new LlmClient(client, model));

    public Task<string> GetModelAsync(string role, CancellationToken cancellationToken) => Task.FromResult(model);
}

/// <summary>Le vrai exécuteur Agent Framework, branché sur un faux modèle et un workspace temporaire.</summary>
public sealed class ScriptedModelApiFactory(IChatClient? model, bool withWorkspace = true, int maxIterations = 10, string modelName = "claude-haiku-5-5")
    : ApiFactory
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-ws-").FullName;

    public string Workspace => _workspace;

    protected override void ConfigureAgents(IServiceCollection services)
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        services.RemoveAll<ILlmClientProvider>();
        services.AddSingleton<ILlmClientProvider>(new FixedLlmClientProvider(model, modelName));
        // Enregistré après la lecture de la configuration : ces valeurs l'emportent sur les user-secrets.
        services.Configure<AgentsOptions>(options =>
        {
            options.Workspace = withWorkspace ? _workspace : null;
            options.MaxIterations = maxIterations;
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_workspace, recursive: true);
    }
}
