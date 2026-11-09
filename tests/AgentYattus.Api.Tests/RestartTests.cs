using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

/// <summary>Après un redémarrage, les missions interrompues repartent (leçon 121).</summary>
public sealed class RestartTests
{
    [Fact]
    public async Task Historical_local_database_is_upgraded_additively_and_can_be_opened_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new MissionsDb(new DbContextOptionsBuilder<MissionsDb>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
        var id = Guid.NewGuid();
        db.Missions.Add(new Mission { Id = id, Goal = "À conserver", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE "Notices";
            DROP TABLE "Drafts";
            ALTER TABLE "Missions" DROP COLUMN "InterlocutorJson";
            ALTER TABLE "Missions" DROP COLUMN "NeedsReply";
            ALTER TABLE "Missions" DROP COLUMN "ResultAccepted";
            ALTER TABLE "Missions" DROP COLUMN "ExecutionId";
            ALTER TABLE "Missions" DROP COLUMN "RecruitmentMissionId";
            ALTER TABLE "Turns" DROP COLUMN "AuthorJson";
            """, ct);
        await LocalConversationSchema.UpgradeAsync(db, ct);
        await LocalConversationSchema.UpgradeAsync(db, ct);
        Assert.Equal("À conserver", (await db.Missions.SingleAsync(ct)).Goal);
        Assert.Empty(await db.Notices.ToListAsync(ct));
        Assert.Empty(await db.Drafts.ToListAsync(ct));
    }

    [Fact(Timeout = 40_000)]
    public async Task Waiting_and_interrupted_missions_resume_after_a_restart()
    {
        Guid waiting, interrupted, paused;
        string db;
        // Premier démarrage : une mission en file, une coupée en cours, une en pause sans décision.
        await using (var before = new ApiFactory { KeepDatabase = true })
        {
            db = before.DatabasePath;
            var root = As(before, "root", instanceAdmin: true);
            await root.GetAsync(IdentityProtocol.Me, TestContext.Current.CancellationToken);
            await using var scope = before.Services.CreateAsyncScope();
            var missions = scope.ServiceProvider.GetRequiredService<MissionsDb>();
            Mission Add(string goal, MissionStatus status)
            {
                var m = new Mission { Id = Guid.CreateVersion7(), Goal = goal, Status = status, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "root" };
                missions.Missions.Add(m);
                return m;
            }
            waiting = Add("En file", MissionStatus.Pending).Id;
            interrupted = Add("Coupée", MissionStatus.Running).Id;
            paused = Add("En pause", MissionStatus.AwaitingApproval).Id;
            await missions.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Deuxième démarrage, sur la même base.
        await using var after = new ApiFactory { DatabasePath = db };
        var client = As(after, "root", instanceAdmin: true);
        await client.WaitForStatusAsync(waiting, MissionStatus.Completed);
        await client.WaitForStatusAsync(interrupted, MissionStatus.Completed);

        var stillPaused = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<MissionDto>(client, $"/missions/{paused}", TestContext.Current.CancellationToken);
        Assert.Equal(MissionStatus.AwaitingApproval, stillPaused!.Status);
        Assert.Contains("reprise", await after.AuditOfAsync(interrupted));
        Assert.DoesNotContain("reprise", await after.AuditOfAsync(waiting));
    }
}
