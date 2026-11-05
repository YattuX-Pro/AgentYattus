using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgentYattus.Api;

internal static class Database
{
    public static IServiceCollection AddModuleDb<TDb>(this IServiceCollection services) where TDb : DbContext
    {
        services.AddDbContext<TDb>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("AgentYattus")));
        return services.AddHostedService<SchemaCreator<TDb>>();
    }

    // EnsureCreated ne crée rien si la base contient déjà des tables, même celles d'un autre module :
    // chaque module crée donc ses propres tables.
    // ponytail: pas de migrations, le schéma n'évolue pas ; passer aux migrations EF dès qu'une table change.
    private sealed class SchemaCreator<TDb>(IServiceScopeFactory scopes, ILogger<TDb> logger) : IHostedService where TDb : DbContext
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var creator = scope.ServiceProvider.GetRequiredService<TDb>().GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(cancellationToken))
                await creator.CreateAsync(cancellationToken);
            try
            {
                await creator.CreateTablesAsync(cancellationToken);
            }
            catch (DbException ex)
            {
                // En général : tables déjà créées au démarrage précédent. Journalisé pour ne pas masquer une vraie erreur.
                logger.LogWarning(ex, "Création des tables de {Db} ignorée", typeof(TDb).Name);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
