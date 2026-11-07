using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core;

public sealed class ApiClient(HttpClient http)
{
    /// <summary>Les visages de l'équipe, partagés par tous les écrans (un seul cache par client).</summary>
    public FaceBook Faces => field ??= new FaceBook(this);

    /// <summary>Réussite et coût de chaque agent ; null si l'on n'a pas le droit de les voir (réservé à llm-admin).</summary>
    public async Task<IReadOnlyList<AgentPerformanceDto>?> GetPerformanceAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(AgentsProtocol.Performance.TrimStart('/'), cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentPerformanceDto[]>(cancellationToken);
    }

    /// <param name="projectId">Le projet de la mission ; null : le projet personnel (ADR 0009).</param>
    /// <param name="agent">L'agent qui fait une mission Question (ADR 0013) ; null : l'Analyste.</param>
    public async Task<MissionDto> CreateMissionAsync(
        string goal, CancellationToken cancellationToken = default, MissionKind kind = MissionKind.Question, Guid? projectId = null,
        string? agent = null)
    {
        using var response = await http.PostAsJsonAsync("missions", new CreateMissionRequest(goal, kind, projectId, agent), cancellationToken);
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

    /// <summary>Où l'on peut choisir le dossier de ses projets (ADR 0014).</summary>
    public async Task<WorkspaceRootsDto> GetWorkspaceRootsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<WorkspaceRootsDto>(ProjectsProtocol.WorkspaceRoots.TrimStart('/'), cancellationToken) ?? new(false, []);

    public async Task SetWorkspaceAsync(Guid project, string? path, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(ProjectsProtocol.Workspace(project).TrimStart('/'), new SetWorkspaceRequest(path), cancellationToken);
        await EnsureOkAsync(response, cancellationToken);
    }

    /// <summary>Copie un fichier de cette machine dans le dossier du projet, sous « pieces-jointes/ » (leçon 115).</summary>
    public async Task<AttachmentDto> UploadAttachmentAsync(Guid project, string localPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(localPath);
        using var form = new MultipartFormDataContent { { new StreamContent(stream), "file", Path.GetFileName(localPath) } };
        using var response = await http.PostAsync(ProjectsProtocol.Attachments(project).TrimStart('/'), form, cancellationToken);
        await EnsureOkAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AttachmentDto>(cancellationToken))!;
    }

    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path.TrimStart('/'), cancellationToken) ?? [];

    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path.TrimStart('/'), cancellationToken) ?? [];

    /// <summary>L'équipe d'un projet et tout ce qu'il faut pour jouer la scène (répliques, vitesse, chef d'équipe).</summary>
    public async Task<ProjectTeamDto?> GetProjectTeamAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(TeamProtocol.ProjectTeam(projectId).TrimStart('/'), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectTeamDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<PortraitDto>> GetPortraitsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<PortraitDto[]>(TeamProtocol.Portraits.TrimStart('/'), cancellationToken) ?? [];

    public async Task<IReadOnlyList<TraitDto>> GetTraitsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<TraitDto[]>(TeamProtocol.Traits.TrimStart('/'), cancellationToken) ?? [];

    /// <summary>Les octets d'une pose (« travail » ou « parle ») ; null si le portrait n'existe plus.</summary>
    public async Task<byte[]?> GetPortraitImageAsync(Guid portraitId, string pose, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(TeamProtocol.Image(portraitId, pose).TrimStart('/'), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<ColleagueDto> RecruitAsync(Guid projectId, RecruitRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(TeamProtocol.ProjectColleagues(projectId).TrimStart('/'), request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ColleagueDto>(cancellationToken))!;
    }

    /// <summary>Un agent à soi, créé depuis « Nouveau collègue » : visible de son auteur seul.</summary>
    public async Task<AgentDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(AgentsProtocol.Path.TrimStart('/'), request, cancellationToken);
        await EnsureOkAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AgentDto>(cancellationToken))!;
    }

    /// <summary>Régler un agent à soi : nom, outils, consigne et modèle.</summary>
    /// <param name="instructions">Null : garder sa consigne d'origine (une consigne héritée, cachée, reste telle quelle).</param>
    public async Task UpdateMyAgentAsync(
        string role, UpdateAgentProfileRequest profile, string? instructions, LlmSelectionDto? model, CancellationToken cancellationToken = default)
    {
        using (var r = await http.PutAsJsonAsync(AgentsProtocol.Profile(role).TrimStart('/'), profile, cancellationToken))
            await EnsureOkAsync(r, cancellationToken);
        using (var r = await http.PutAsJsonAsync(AgentsProtocol.Role(role).TrimStart('/'), new UpdateAgentRequest(instructions, null), cancellationToken))
            await EnsureOkAsync(r, cancellationToken);
        using (var r = await http.PutAsJsonAsync($"agents/{role}/model", new UpdateAgentModelRequest(model), cancellationToken))
            await EnsureOkAsync(r, cancellationToken);
    }

    // Modèles et clés : en mode local, il n'y a pas d'espace admin web, ils se règlent dans le logiciel (ADR 0014).
    public async Task<LlmStatusDto> GetLlmStatusAsync(CancellationToken cancellationToken = default) =>
        (await http.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path.TrimStart('/'), cancellationToken))!;

    public async Task<LlmModelsDto> GetLlmModelsAsync(CancellationToken cancellationToken = default) =>
        (await http.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models.TrimStart('/'), cancellationToken))!;

    /// <returns>Null si tout va bien, sinon le message d'erreur renvoyé par l'API.</returns>
    public async Task<string?> ConnectLlmAsync(AddLlmConnectionRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(LlmProtocol.Connections.TrimStart('/'), request, cancellationToken);
        return await ErrorOrNullAsync(response, cancellationToken);
    }

    public async Task RevokeLlmConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{LlmProtocol.Connections.TrimStart('/')}/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <param name="model">Null pour revenir au modèle par défaut.</param>
    public async Task<string?> SetAgentModelAsync(string role, LlmSelectionDto? model, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(AgentsProtocol.Model(role).TrimStart('/'), new UpdateAgentModelRequest(model), cancellationToken);
        return await ErrorOrNullAsync(response, cancellationToken);
    }

    public async Task<string?> UpdateFreeModeAsync(UpdateFreeModeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(LlmProtocol.Free.TrimStart('/'), request, cancellationToken);
        return await ErrorOrNullAsync(response, cancellationToken);
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


    public async Task<IReadOnlyList<ToolInfoDto>> GetToolsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ToolInfoDto[]>(AgentsProtocol.Tools.TrimStart('/'), cancellationToken) ?? [];

    /// <summary>Les compétences qu'on peut donner à ses collègues : les siennes et celles de l'instance (leçon 104).</summary>
    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<SkillDto[]>(SkillsProtocol.Path.TrimStart('/'), cancellationToken) ?? [];

    /// <summary>Par compétence : missions qui l'ont chargée, et combien ont abouti (leçon 107).</summary>
    public async Task<IReadOnlyList<SkillPerformanceDto>> GetSkillPerformanceAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<SkillPerformanceDto[]>(SkillsProtocol.Performance.TrimStart('/'), cancellationToken) ?? [];

    public async Task<SkillDto> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(SkillsProtocol.Path.TrimStart('/'), request, cancellationToken);
        await EnsureOkAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SkillDto>(cancellationToken))!;
    }

    /// <summary>Les modèles qu'on peut donner à ses agents (connexions de l'instance).</summary>
    public async Task<IReadOnlyList<LlmModelDto>> GetModelChoicesAsync(CancellationToken cancellationToken = default) =>
        (await http.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Choices.TrimStart('/'), cancellationToken))?.Models ?? [];

    // Un refus de validation : ses messages, lisibles, plutôt que « 400 Bad Request ».
    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = "";
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.TryGetProperty("errors", out var errors))
                detail = string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(m => m.GetString())));
        }
        catch (System.Text.Json.JsonException)
        {
        }
        throw new HttpRequestException(detail.Length > 0 ? detail : $"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
    }

    public async Task<MissionDto?> GetMissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"missions/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MissionDto>(cancellationToken);
    }
}
