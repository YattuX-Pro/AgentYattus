using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core;

public sealed class ApiClient(HttpClient http)
{
    public async Task<MissionDto> CreateMissionAsync(
        string goal, CancellationToken cancellationToken = default, MissionKind kind = MissionKind.Question)
    {
        using var response = await http.PostAsJsonAsync("missions", new CreateMissionRequest(goal, kind), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MissionDto>(cancellationToken))!;
    }

    public async Task<IReadOnlyList<MissionDto>> ListMissionsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<MissionDto[]>($"missions?limit={limit}", cancellationToken) ?? [];

    public async Task<IReadOnlyList<AuditEntryDto>> GetEventsAsync(Guid missionId, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AuditEntryDto[]>($"missions/{missionId}/events", cancellationToken) ?? [];

    /// <returns>Faux si la décision n'est plus possible : déjà prise, ou la mission n'attend rien (409).</returns>
    public async Task<bool> DecideAsync(Guid missionId, Guid approvalId, bool approved, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"missions/{missionId}/decision", new DecisionRequest(approved, approvalId), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path.TrimStart('/'), cancellationToken) ?? [];

    public async Task<LlmStatusDto> GetLlmStatusAsync(CancellationToken cancellationToken = default) =>
        (await http.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path.TrimStart('/'), cancellationToken))!;

    /// <returns>Null si tout va bien, sinon le message d'erreur renvoyé par l'API.</returns>
    public async Task<string?> AddLlmCredentialAsync(AddLlmCredentialRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(LlmProtocol.Credentials.TrimStart('/'), request, cancellationToken);
        return await ErrorOrNullAsync(response, cancellationToken);
    }

    public async Task RevokeLlmCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{LlmProtocol.Credentials.TrimStart('/')}/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> UpdateLlmSettingsAsync(UpdateLlmSettingsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(LlmProtocol.Settings.TrimStart('/'), request, cancellationToken);
        return await ErrorOrNullAsync(response, cancellationToken);
    }

    // Une erreur de validation (400) devient un message lisible ; les autres erreurs restent des exceptions.
    private static async Task<string?> ErrorOrNullAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
            return null;
        }

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken);
        return string.Join(" ", problem?.Errors?.SelectMany(e => e.Value) ?? ["Requête refusée."]);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);

    public async Task<MissionDto?> GetMissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"missions/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MissionDto>(cancellationToken);
    }
}
