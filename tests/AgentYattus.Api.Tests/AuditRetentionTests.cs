using AgentYattus.Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentYattus.Api.Tests;

public sealed class AuditRetentionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Purging_removes_only_events_older_than_the_cutoff()
    {
        await using var api = new ApiFactory();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDb>();
        var mission = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Events.AddRange(
            new AuditEvent { MissionId = mission, Type = "ancien-1", At = now.AddDays(-200) },
            new AuditEvent { MissionId = mission, Type = "ancien-2", At = now.AddDays(-100) },
            new AuditEvent { MissionId = mission, Type = "récent", At = now.AddDays(-10) });
        await db.SaveChangesAsync(Ct);

        var removed = await scope.ServiceProvider.GetRequiredService<AuditLog>().PurgeOlderThanAsync(now.AddDays(-90), Ct);

        Assert.Equal(2, removed);
        Assert.Equal(["récent"], await db.Events.Where(e => e.MissionId == mission).Select(e => e.Type).ToListAsync(Ct));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditLog>().PurgeOlderThanAsync(now.AddDays(-90), Ct));
    }
}
