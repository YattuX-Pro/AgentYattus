using System.Collections.Concurrent;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            UseSqlite<MissionsDb>(services);
            UseSqlite<AuditDb>(services);
            // Jamais d'appel au vrai modèle pendant les tests, même si une clé est dans les user-secrets.
            services.RemoveAll<IChatClient>();
            ConfigureAgents(services);
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

public sealed class FakeAgentRunner : IAgentRunner
{
    public const string TimeoutGoal = "simule un délai HTTP dépassé";

    public ConcurrentQueue<string> Goals { get; } = new();

    public Task<AgentOutcome> RunAsync(Guid missionId, string goal, AgentResume? resume, CancellationToken cancellationToken)
    {
        Goals.Enqueue(goal);
        // Ce que lèvera un HttpClient en délai dépassé dans le vrai runner.
        return goal == TimeoutGoal
            ? Task.FromException<AgentOutcome>(new TaskCanceledException())
            : Task.FromResult(new AgentOutcome($"Réponse à : {goal}"));
    }
}

/// <summary>Le vrai exécuteur Agent Framework, branché sur un faux modèle et un workspace temporaire.</summary>
public sealed class ScriptedModelApiFactory(ScriptedChatClient? model, bool withWorkspace = true, int maxIterations = 10)
    : ApiFactory
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-ws-").FullName;

    public string Workspace => _workspace;

    protected override void ConfigureAgents(IServiceCollection services)
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        if (model is not null)
            services.AddSingleton<IChatClient>(model);
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
