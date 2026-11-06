using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using AgentYattus.Contracts;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>Les deux façons de parler à un modèle : l'API d'Anthropic, ou l'API d'OpenAI que presque tous imitent.</summary>
internal enum LlmWire { Anthropic, OpenAI }

/// <param name="BaseUrl">L'adresse de l'API ; imposée pour un fournisseur à clé, proposée pour un serveur local.</param>
/// <param name="Notice">Ce qu'il faut savoir avant de s'y connecter (offre gratuite, usage des données).</param>
internal sealed record LlmProvider(string Id, string Name, LlmConnectionKind Kind, LlmWire Wire, string? BaseUrl, string? Notice = null);

/// <summary>Les fournisseurs branchés, comme dans opencode : à clé d'API, locaux, ou compatibles OpenAI à l'adresse de son choix.</summary>
internal static class LlmProviders
{
    public static readonly LlmProvider Anthropic = new("anthropic", "Anthropic", LlmConnectionKind.ApiKey, LlmWire.Anthropic, null);

    public static IReadOnlyList<LlmProvider> All { get; } =
    [
        Anthropic,
        new("openai", "OpenAI", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://api.openai.com/v1"),
        new("google", "Google Gemini", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://generativelanguage.googleapis.com/v1beta/openai/",
            "Offre gratuite à quota. En offre gratuite, Google peut utiliser tes requêtes pour améliorer ses produits."),
        new("mistral", "Mistral", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://api.mistral.ai/v1"),
        new("groq", "Groq", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://api.groq.com/openai/v1",
            "Offre gratuite à quota (limites par minute et par jour)."),
        new("deepseek", "DeepSeek", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://api.deepseek.com/v1"),
        new("xai", "xAI", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://api.x.ai/v1"),
        new("openrouter", "OpenRouter", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://openrouter.ai/api/v1",
            "Les modèles « :free » sont limités en débit, et leurs hébergeurs peuvent conserver tes requêtes."),
        new("opencode", "OpenCode Zen", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://opencode.ai/inference/openai/v1",
            "Modèles gratuits pour une durée limitée ; tes requêtes peuvent servir à améliorer ces modèles."),
        new("nvidia", "NVIDIA", LlmConnectionKind.ApiKey, LlmWire.OpenAI, "https://integrate.api.nvidia.com/v1",
            "Accès gratuit d'essai, limité en débit."),
        new("ollama", "Ollama", LlmConnectionKind.Local, LlmWire.OpenAI, "http://localhost:11434/v1"),
        new("lmstudio", "LM Studio", LlmConnectionKind.Local, LlmWire.OpenAI, "http://127.0.0.1:1234/v1"),
        new("openai-compatible", "Compatible OpenAI", LlmConnectionKind.Custom, LlmWire.OpenAI, null),
    ];

    public static LlmProvider? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    public static IChatClient CreateClient(LlmProvider provider, string? apiKey, string baseUrl, string model) => provider.Wire switch
    {
        LlmWire.Anthropic => new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000),
        // Un serveur local n'a souvent pas de clé, mais le client OpenAI en exige une : n'importe quelle valeur convient.
        _ => new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "local" : apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(baseUrl), Transport = new HttpClientPipelineTransport(GuardedHttp) })
            .GetChatClient(model).AsIChatClient(),
    };

    // Les appels aux modèles passent par la même garde que la découverte des modèles.
    // Pas de délai ici : celui du client OpenAI s'applique.
    private static readonly HttpClient GuardedHttp = new(CreateGuardedHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Un gestionnaire HTTP qui ne suit pas les redirections, et qui vérifie l'adresse réellement contactée
    /// après la résolution DNS : un nom qui changerait d'adresse entre la vérification et l'appel (DNS rebinding) est bloqué.
    /// </summary>
    public static SocketsHttpHandler CreateGuardedHandler() => new()
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(IsForbidden))
                throw new HttpRequestException($"Adresse non autorisée : {context.DnsEndPoint.Host}.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // On se connecte aux adresses vérifiées, pas à une nouvelle résolution du nom.
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>Link-local (dont les métadonnées des clouds) et adresses « quelconques » : jamais. Le réseau privé reste permis.</summary>
    public static bool IsForbidden(IPAddress ip)
    {
        // ::ffff:169.254.169.254 est la même adresse que 169.254.169.254.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        return ip.IsIPv6LinkLocal || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
            || (ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes() is [169, 254, ..]);
    }

    /// <summary>
    /// L'adresse d'un serveur local ou personnalisé. La clé d'API y sera envoyée : HTTPS obligatoire,
    /// sauf sur cette machine (localhost), où rien ne passe par le réseau.
    /// </summary>
    public static string? ValidateBaseUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "L'adresse doit commencer par https:// (ou http:// sur cette machine).";
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return "L'adresse ne doit contenir ni identifiants, ni paramètres.";
        // ponytail: HTTP limité à localhost ; ouvrir aux adresses du réseau local si un serveur Ollama partagé le demande.
        if (uri.Scheme == "http" && !uri.IsLoopback)
            return "HTTP n'est accepté que sur cette machine (localhost) : utilise https://.";
        // Les adresses link-local (169.254.x.x, fe80::) servent notamment les métadonnées des clouds : jamais.
        // Vérifié ici pour un message clair, et de nouveau à la connexion, après la résolution DNS.
        if (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && IsForbidden(ip))
            return "Cette adresse n'est pas autorisée.";
        return null;
    }
}
