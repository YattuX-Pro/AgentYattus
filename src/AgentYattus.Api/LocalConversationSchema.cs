using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api;

/// <summary>Mise à niveau additive du mode SQLite historique, qui utilisait EnsureCreated.</summary>
internal static class LocalConversationSchema
{
    public static async Task UpgradeAsync(DbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        if (tables.Contains("Missions"))
        {
            await ColumnAsync(db, "Missions", "InterlocutorJson", "TEXT NULL", ct);
            await ColumnAsync(db, "Missions", "RecruitmentMissionId", "TEXT NULL", ct);
            await ColumnAsync(db, "Missions", "ExecutionId", "TEXT NULL", ct);
            await ColumnAsync(db, "Missions", "ResultAccepted", "INTEGER NOT NULL DEFAULT 0", ct);
            await ColumnAsync(db, "Missions", "NeedsReply", "INTEGER NOT NULL DEFAULT 0", ct);
            await ColumnAsync(db, "Turns", "AuthorJson", "TEXT NULL", ct);
        }
        if (tables.Contains("Portraits"))
        {
            await ColumnAsync(db, "Portraits", "MouthX", "REAL NOT NULL DEFAULT 0.56", ct);
            await ColumnAsync(db, "Portraits", "MouthY", "REAL NOT NULL DEFAULT 0.37", ct);
            await ColumnAsync(db, "Colleagues", "RecruitmentSource", "TEXT NULL", ct);
        }
        if (!tables.Contains("Missions") && !tables.Contains("Portraits")) return;
        var script = db.Database.GenerateCreateScript()
            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync(script, ct);
    }
    private static async Task ColumnAsync(DbContext db, string table, string column, string declaration, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        var exists = false; var found = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) { exists = true; found |= reader.GetString(1) == column; }
        if (exists && !found)
        {
            // Identifiants et déclaration constants définis dans ce fichier, sans entrée utilisateur.
            command.CommandText = $"""ALTER TABLE "{table}" ADD COLUMN "{column}" {declaration}""";
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
