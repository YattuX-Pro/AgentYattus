using System.Net.Http.Json;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentYattus.Api.Tests;

/// <summary>Raccourcis partagés par les tests : créer une mission, attendre sa fin, lire son audit.</summary>
internal static class MissionsApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<MissionDto> CreateMissionAsync(this HttpClient client, string goal, MissionKind kind = MissionKind.Question, string? agent = null)
    {
        var response = await client.PostAsJsonAsync("/missions", new CreateMissionRequest(goal, kind, Agent: agent), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MissionDto>(Ct))!;
    }

    public static async Task<MissionStatus> WaitForFinalStatusAsync(this HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = (await client.GetFromJsonAsync<MissionDto>($"/missions/{id}", Ct))!.Status;
            if (status is MissionStatus.Completed or MissionStatus.Failed)
                return status;
            Assert.True(DateTime.UtcNow < deadline, $"Mission {id} bloquée en {status}.");
            await Task.Delay(20, Ct);
        }
    }

    public static async Task<MissionDto> WaitForStatusAsync(this HttpClient client, Guid id, MissionStatus expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var mission = (await client.GetFromJsonAsync<MissionDto>($"/missions/{id}", Ct))!;
            if (mission.Status == expected)
                return mission;
            Assert.True(DateTime.UtcNow < deadline, $"Mission {id} en {mission.Status}, {expected} attendu.");
            await Task.Delay(20, Ct);
        }
    }

    public static async Task<List<string>> AuditOfAsync(this WebApplicationFactory<Program> factory, Guid missionId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDb>().Events
            .Where(e => e.MissionId == missionId).OrderBy(e => e.Id).Select(e => e.Type).ToListAsync(Ct);
    }
}
