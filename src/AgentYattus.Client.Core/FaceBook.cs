using AgentYattus.Contracts;

namespace AgentYattus.Client.Core;

/// <summary>Un visage : le prénom du collègue et ses deux poses (« travaille », « parle »).</summary>
public sealed record Face(string Name, byte[]? Work, byte[]? Talk);

/// <summary>
/// Les visages de l'équipe (ADR 0012), partout où l'on parle d'un agent : accueil, missions, agents, rapports.
/// Chaque équipe et chaque image ne sont demandées qu'une fois.
/// </summary>
/// <remarks>
/// ponytail: un échec réseau reste en cache (pas de visage jusqu'au prochain <see cref="Forget"/>) ;
/// à revoir si les coupures deviennent fréquentes.
/// </remarks>
public sealed class FaceBook(ApiClient api)
{
    private readonly Dictionary<Guid, Task<ProjectTeamDto?>> _teams = [];
    private readonly Dictionary<(Guid, string), Task<byte[]?>> _images = [];

    /// <summary>Le projet personnel : celui des missions qui n'en précisent pas.</summary>
    public Guid? DefaultProject { get; set; }

    /// <summary>Le collègue qui occupe ce rôle dans le projet ; null : siège libre.</summary>
    public async Task<Face?> AgentAsync(Guid? project, string role)
    {
        var colleague = (await TeamAsync(project))?.Colleagues.FirstOrDefault(c => c.Active && c.Role == role);
        return colleague is null ? null : await FaceAsync(colleague.Name, colleague.PortraitId);
    }

    /// <summary>« Vous » : le portrait du responsable, qui décide.</summary>
    public async Task<Face?> ManagerAsync(Guid? project) =>
        await TeamAsync(project) is { ManagerPortraitId: { } id } ? await FaceAsync("Vous", id) : null;

    /// <summary>L'équipe du projet (null : projet personnel), lue une fois.</summary>
    public Task<ProjectTeamDto?> TeamOfAsync(Guid? project) => TeamAsync(project);

    /// <summary>Après un recrutement : l'équipe du projet sera relue.</summary>
    public void Forget(Guid project) => _teams.Remove(project);

    private Task<ProjectTeamDto?> TeamAsync(Guid? project)
    {
        if ((project ?? DefaultProject) is not { } id)
            return Task.FromResult<ProjectTeamDto?>(null);
        if (!_teams.TryGetValue(id, out var team))
            _teams[id] = team = OrNull(() => api.GetProjectTeamAsync(id));
        return team;
    }

    public async Task<Face?> HistoricalAsync(MessageAuthorDto? author)
    {
        if (author is null) return null;
        return author.PortraitId is { } portrait ? await FaceAsync(author.Name, portrait) : new Face(author.Name, null, null);
    }

    private async Task<Face> FaceAsync(string name, Guid portrait) =>
        new(name, await ImageAsync(portrait, "travail"), await ImageAsync(portrait, "parle"));

    private Task<byte[]?> ImageAsync(Guid portrait, string pose)
    {
        if (!_images.TryGetValue((portrait, pose), out var image))
            _images[(portrait, pose)] = image = OrNull(() => api.GetPortraitImageAsync(portrait, pose));
        return image;
    }

    // Un visage manquant n'empêche rien : l'écran affiche les initiales.
    private static async Task<T?> OrNull<T>(Func<Task<T?>> fetch) where T : class
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}
