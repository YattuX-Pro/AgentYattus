using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>Ce que l'on sait d'un modèle : de quoi l'afficher, l'estimer, et vérifier qu'il sait appeler des outils.</summary>
internal sealed record CatalogModel(string Id, string Name, int? ContextWindow, decimal? InputPrice, decimal? OutputPrice, bool Reasoning)
{
    public bool Free => InputPrice == 0 && OutputPrice == 0;
}

/// <summary>
/// Le catalogue des modèles, tiré de models.dev comme dans opencode. Une copie est embarquée dans l'API
/// (scripts/update-models-snapshot.py) ; <see cref="CatalogRefresher"/> la remplace par la version en ligne quand elle répond.
/// </summary>
internal sealed partial class ModelCatalog
{
    public const string HttpClientName = "llm";
    private const int MaxModelsPerServer = 500;
    private const int MaxDiscoveryBytes = 1_000_000;

    private volatile IReadOnlyDictionary<string, IReadOnlyList<CatalogModel>> _models;

    public ModelCatalog()
    {
        using var snapshot = typeof(ModelCatalog).Assembly.GetManifestResourceStream("models-snapshot.json")!;
        _models = Parse(JsonDocument.Parse(snapshot));
    }

    public IReadOnlyList<CatalogModel> ModelsOf(string provider) => _models.GetValueOrDefault(provider) ?? [];

    public CatalogModel? Find(string provider, string model) => ModelsOf(provider).FirstOrDefault(m => m.Id == model);

    /// <summary>Remplace le catalogue par celui de <paramref name="url"/> ; en cas d'échec, l'ancien reste.</summary>
    public async Task RefreshAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(url, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var models = Parse(document);
        if (models.Count > 0)
            _models = models;
    }

    /// <summary>
    /// Les modèles installés sur un serveur compatible OpenAI (Ollama, LM Studio…) : <c>GET {adresse}/models</c>.
    /// On ne connaît ni leur prix ni leur contexte ; un modèle local est gratuit.
    /// </summary>
    public static async Task<IReadOnlyList<CatalogModel>> DiscoverAsync(
        HttpClient http, string baseUrl, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        // Une liste de modèles tient en quelques kilo-octets : au-delà, on refuse de lire.
        if (response.Content.Headers.ContentLength > MaxDiscoveryBytes)
            throw new InvalidOperationException("Réponse trop volumineuse.");
        // On lit au plus la limite (plus un octet pour la détecter), même si le serveur n'annonce pas de taille.
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = new byte[MaxDiscoveryBytes + 1];
        var read = 0;
        while (read < bytes.Length && await body.ReadAsync(bytes.AsMemory(read), cancellationToken) is var n and > 0)
            read += n;
        if (read > MaxDiscoveryBytes)
            throw new InvalidOperationException("Réponse trop volumineuse.");
        var buffer = new MemoryStream(bytes, 0, read);
        using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
        return [.. document.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
            .Where(IsValidId).Distinct().Take(MaxModelsPerServer)
            .Select(id => new CatalogModel(id!, id!, null, 0, 0, false))];
    }

    // Les identifiants viennent de l'extérieur : on n'accepte que des caractères sûrs, et une longueur bornée.
    public static bool IsValidId(string? id) => id is not null && SafeId().IsMatch(id);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/@+\-]{0,199}$")]
    private static partial Regex SafeId();

    // Le format de models.dev : { "<fournisseur>": { "models": { "<id>": { name, tool_call, reasoning, limit, cost } } } }.
    private static Dictionary<string, IReadOnlyList<CatalogModel>> Parse(JsonDocument document)
    {
        var result = new Dictionary<string, IReadOnlyList<CatalogModel>>();
        foreach (var provider in LlmProviders.All.Where(p => p.Kind == Contracts.LlmConnectionKind.ApiKey))
        {
            // Le catalogue vient d'Internet : on vérifie la forme de chaque niveau avant de le lire.
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(provider.Id, out var p) || p.ValueKind != JsonValueKind.Object
                || !p.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
                continue;
            var list = new List<CatalogModel>();
            foreach (var m in models.EnumerateObject())
            {
                // Un agent sans outils ne peut rien faire : on ne propose que les modèles qui savent en appeler.
                if (m.Value.ValueKind != JsonValueKind.Object || !IsValidId(m.Name) || !Bool(m.Value, "tool_call")
                    || Text(m.Value, "status") == "deprecated")
                    continue;
                var name = Text(m.Value, "name") is { Length: > 0 } n ? new string([.. n.Where(c => !char.IsControl(c)).Take(100)]) : m.Name;
                list.Add(new CatalogModel(m.Name, name, (int?)Number(m.Value, "limit", "context"),
                    Number(m.Value, "cost", "input"), Number(m.Value, "cost", "output"), Bool(m.Value, "reasoning")));
            }

            result[provider.Id] = [.. list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
        }

        return result;
    }

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? Number(JsonElement e, string group, string name) =>
        e.TryGetProperty(group, out var g) && g.ValueKind == JsonValueKind.Object && g.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d is >= 0 and < 100_000_000 ? d : null;
}

/// <summary>Met le catalogue à jour depuis models.dev au démarrage, puis chaque jour. Désactivé si <c>Llm:CatalogUrl</c> est vide.</summary>
internal sealed class CatalogRefresher(ModelCatalog catalog, IHttpClientFactory http, IConfiguration configuration, ILogger<CatalogRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration["Llm:CatalogUrl"] is not { Length: > 0 } url)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await catalog.RefreshAsync(http.CreateClient(ModelCatalog.HttpClientName), url, stoppingToken);
                logger.LogInformation("Catalogue de modèles mis à jour depuis {Url}", url);
            }
            // Quelle que soit l'erreur, un mauvais catalogue ne doit jamais arrêter l'API.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Catalogue de modèles injoignable ({Error}) : la copie embarquée reste utilisée", ex.Message);
            }

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
