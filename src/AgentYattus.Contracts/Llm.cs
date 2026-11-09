namespace AgentYattus.Contracts;

public static class LlmProtocol
{
    public const string Path = "/llm";
    public const string Connections = "/llm/connections";
    public const string Models = "/llm/models";

    /// <summary>Les modèles qu'un utilisateur peut donner à ses agents : ceux des connexions de l'instance, sans rien de secret.</summary>
    public const string Choices = "/llm/choices";
    public const string Settings = "/llm/settings";
    public const string Free = "/llm/free";

    /// <summary>Les clés personnelles (leçon 134) : chacun gère les siennes ; elles ne servent qu'à ses propres missions.</summary>
    public const string Mine = "/llm/mine";
    public const string MineConnections = "/llm/mine/connections";
    public const string MineDefault = "/llm/mine/default";
    public const string MineFree = "/llm/mine/free";
    public const string MineModels = "/llm/mine/models";
}

/// <summary>Comment on se connecte à un fournisseur : une clé d'API, un serveur local, ou une adresse au choix.</summary>
public enum LlmConnectionKind { ApiKey, Local, Custom }

/// <summary>Un fournisseur que l'on peut connecter.</summary>
/// <param name="Notice">Ce qu'il faut savoir avant de s'y connecter (offre gratuite à quota, usage des données).</param>
public sealed record LlmProviderDto(string Id, string Name, LlmConnectionKind Kind, string? DefaultBaseUrl, int ModelCount, string? Notice = null);

/// <summary>
/// Un fournisseur connecté. La clé n'est jamais renvoyée : seulement ses 4 derniers caractères, pour la reconnaître.
/// </summary>
/// <param name="Personal">Une clé personnelle (leçon 134) : elle ne sert qu'aux missions de son propriétaire.</param>
public sealed record LlmConnectionDto(
    Guid Id, string Provider, string ProviderName, string Label, string? Last4, string? BaseUrl,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? RevokedAt, bool Personal = false);

/// <summary>
/// Un modèle utilisable par une connexion. Prix en dollars par million de tokens ; null quand on ne le connaît pas
/// (serveur local, adresse personnalisée).
/// </summary>
/// <param name="Personal">Par une clé personnelle de la personne qui demande (leçon 134).</param>
public sealed record LlmModelDto(
    Guid ConnectionId, string Provider, string Id, string Name, int? ContextWindow,
    decimal? InputPricePerMillion, decimal? OutputPricePerMillion, bool Reasoning, bool Free, bool Personal = false);

/// <summary>Une connexion dont on n'a pas pu lister les modèles (serveur local éteint, par exemple).</summary>
public sealed record LlmConnectionErrorDto(Guid ConnectionId, string Message);

public sealed record LlmModelsDto(IReadOnlyList<LlmModelDto> Models, IReadOnlyList<LlmConnectionErrorDto> Errors);

/// <summary>Un modèle choisi : par quelle connexion, et lequel.</summary>
public sealed record LlmSelectionDto(Guid ConnectionId, string Model);

/// <param name="FreeMode">Mode Gratuit : tous les agents utilisent les modèles gratuits, avec bascule automatique.</param>
public sealed record LlmStatusDto(
    IReadOnlyList<LlmProviderDto> Providers, IReadOnlyList<LlmConnectionDto> Connections, LlmSelectionDto? Default,
    bool FreeMode = false, IReadOnlyList<LlmSelectionDto>? FreeChain = null);

/// <summary>Connecter un fournisseur. <paramref name="ApiKey"/> est facultative pour un serveur local.</summary>
public sealed record AddLlmConnectionRequest(string Provider, string Label, string? ApiKey, string? BaseUrl);

public sealed record UpdateLlmSettingsRequest(LlmSelectionDto Default);

/// <summary>
/// Mes clés (leçon 134) : les fournisseurs à clé que l'on peut connecter soi-même, ses connexions, et son modèle par défaut.
/// Ce modèle passe avant ceux de l'instance pour ses propres missions, et seulement pour elles.
/// </summary>
public sealed record MyLlmDto(IReadOnlyList<LlmProviderDto> Providers, IReadOnlyList<LlmConnectionDto> Connections, LlmSelectionDto? Default, bool FreeMode = false);

public sealed record UpdateMyFreeModeRequest(bool Enabled);

/// <param name="Default">Null : plus de modèle personnel, les missions reprennent celui de l'instance.</param>
public sealed record UpdateMyDefaultRequest(LlmSelectionDto? Default);

/// <summary>Le mode Gratuit : activé ou non, et la chaîne de modèles gratuits, dans l'ordre où les essayer.</summary>
public sealed record UpdateFreeModeRequest(bool Enabled, IReadOnlyList<LlmSelectionDto> Chain);
