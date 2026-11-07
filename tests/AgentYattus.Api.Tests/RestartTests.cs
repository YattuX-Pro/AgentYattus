using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

/// <summary>Après un redémarrage, les missions interrompues repartent (leçon 121).</summary>
public sealed class RestartTests
{
    [Fact]
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
