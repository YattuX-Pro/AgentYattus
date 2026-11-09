using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgentYattus.Api;

internal static class Database
{
    public static IServiceCollection AddModuleDb<TDb>(this IServiceCollection services, string schema) where TDb : DbContext
    {
        services.AddDbContext<TDb>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            // Mode local (ADR 0014) : un fichier SQLite dans le dossier de données, partagé par tous les modules.
            if (LocalMode.IsOn(configuration))
                options.UseSqlite(LocalMode.ConnectionString(configuration));
            else
                options.UseNpgsql(
                    configuration.GetConnectionString("AgentYattus"),
                    // Chaque module garde l'historique de ses migrations dans son propre schéma.
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        });
        return services.AddHostedService<SchemaUpdater<TDb>>();
    }

    // PostgreSQL : on applique les migrations du module au démarrage.
    // ponytail: SQLite (tests, futur mode local) crée encore ses tables sans migrations ;
    // sa propre série de migrations viendra avec le mode local (ADR 0004).
    private sealed class SchemaUpdater<TDb>(IServiceScopeFactory scopes, ILogger<TDb> logger) : IHostedService where TDb : DbContext
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TDb>();
            if (db.Database.IsNpgsql())
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }

            // EnsureCreated ne crée rien si la base contient déjà des tables, même celles d'un autre module :
            // chaque module crée donc ses propres tables.
            var creator = db.GetService<IRelationalDatabaseCreator>();
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
                await LocalConversationSchema.UpgradeAsync(db, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
