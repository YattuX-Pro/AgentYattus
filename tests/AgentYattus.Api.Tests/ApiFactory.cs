using AgentYattus.Api.Modules.Missions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentYattus.Api.Tests;

// SQLite sur fichier temporaire à la place de PostgreSQL : les tests tournent sans Docker.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agentyattus-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            UseSqlite<MissionsDb>(services);
        });

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
