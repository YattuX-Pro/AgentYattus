namespace AgentYattus.Contracts;

public static class LlmProtocol
{
    public const string Path = "/llm";
    public const string Connections = "/llm/connections";
    public const string Models = "/llm/models";
    public const string Settings = "/llm/settings";
    public const string Free = "/llm/free";
}

/// <summary>Comment on se connecte à un fournisseur : une clé d'API, un serveur local, ou une adresse au choix.</summary>
public enum LlmConnectionKind { ApiKey, Local, Custom }

/// <summary>Un fournisseur que l'on peut connecter.</summary>
/// <param name="Notice">Ce qu'il faut savoir avant de s'y connecter (offre gratuite à quota, usage des données).</param>
public sealed record LlmProviderDto(string Id, string Name, LlmConnectionKind Kind, string? DefaultBaseUrl, int ModelCount, string? Notice = null);

/// <summary>
/// Un fournisseur connecté. La clé n'est jamais renvoyée : seulement ses 4 derniers caractères, pour la reconnaître.
/// </summary>
public sealed record LlmConnectionDto(
    Guid Id, string Provider, string ProviderName, string Label, string? Last4, string? BaseUrl,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? RevokedAt);

/// <summary>
/// Un modèle utilisable par une connexion. Prix en dollars par million de tokens ; null quand on ne le connaît pas
/// (serveur local, adresse personnalisée).
/// </summary>
public sealed record LlmModelDto(
    Guid ConnectionId, string Provider, string Id, string Name, int? ContextWindow,
    decimal? InputPricePerMillion, decimal? OutputPricePerMillion, bool Reasoning, bool Free);

/// <summary>Une connexion dont on n'a pas pu lister les modèles (serveur local éteint, par exemple).</summary>
public sealed record LlmConnectionErrorDto(Guid ConnectionId, string Message);

public sealed record LlmModelsDto(IReadOnlyList<LlmModelDto> Models, IReadOnlyList<LlmConnectionErrorDto> Errors);

/// <summary>Un modèle choisi : par quelle connexion, et lequel.</summary>
public sealed record LlmSelectionDto(Guid ConnectionId, string Model);

/// <param name="FreeMode">Mode Gratuit : les agents sans modèle propre utilisent la chaîne gratuite, avec bascule automatique.</param>
public sealed record LlmStatusDto(
    IReadOnlyList<LlmProviderDto> Providers, IReadOnlyList<LlmConnectionDto> Connections, LlmSelectionDto? Default,
    bool FreeMode = false, IReadOnlyList<LlmSelectionDto>? FreeChain = null);

/// <summary>Connecter un fournisseur. <paramref name="ApiKey"/> est facultative pour un serveur local.</summary>
public sealed record AddLlmConnectionRequest(string Provider, string Label, string? ApiKey, string? BaseUrl);

public sealed record UpdateLlmSettingsRequest(LlmSelectionDto Default);

/// <summary>Le mode Gratuit : activé ou non, et la chaîne de modèles gratuits, dans l'ordre où les essayer.</summary>
public sealed record UpdateFreeModeRequest(bool Enabled, IReadOnlyList<LlmSelectionDto> Chain);
