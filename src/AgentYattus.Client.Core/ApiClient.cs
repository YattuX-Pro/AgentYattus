using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core;

public sealed class ApiClient(HttpClient http)
{
    public async Task<MissionDto> CreateMissionAsync(string goal, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("missions", new CreateMissionRequest(goal), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MissionDto>(cancellationToken))!;
    }

    public async Task<IReadOnlyList<MissionDto>> ListMissionsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<MissionDto[]>($"missions?limit={limit}", cancellationToken) ?? [];

    public async Task<IReadOnlyList<AuditEntryDto>> GetEventsAsync(Guid missionId, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AuditEntryDto[]>($"missions/{missionId}/events", cancellationToken) ?? [];

    public async Task<MissionDto?> GetMissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"missions/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MissionDto>(cancellationToken);
    }
}
