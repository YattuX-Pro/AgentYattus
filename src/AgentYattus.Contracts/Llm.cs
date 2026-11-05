namespace AgentYattus.Contracts;

public static class LlmProtocol
{
    public const string Path = "/llm";
    public const string Credentials = "/llm/credentials";
    public const string Settings = "/llm/settings";
}

/// <summary>Un modèle proposé, avec son prix public en dollars par million de tokens.</summary>
public sealed record LlmModelDto(string Id, string Name, decimal InputPricePerMillion, decimal OutputPricePerMillion);

public sealed record LlmProviderDto(string Id, string Name, IReadOnlyList<LlmModelDto> Models);

/// <summary>
/// Une clé d'API enregistrée. La clé elle-même n'est jamais renvoyée : seulement ses 4 derniers caractères,
/// pour la reconnaître.
/// </summary>
public sealed record LlmCredentialDto(
    Guid Id, string Provider, string Label, string Last4, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset? RevokedAt, bool IsActive);

public sealed record LlmStatusDto(
    IReadOnlyList<LlmProviderDto> Providers, IReadOnlyList<LlmCredentialDto> Credentials, Guid? ActiveCredentialId, string Model);

public sealed record AddLlmCredentialRequest(string Provider, string Label, string ApiKey);

public sealed record UpdateLlmSettingsRequest(Guid? CredentialId, string Model);
