using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentYattus.Client.Core;

/// <summary>Où se connecter : le realm Keycloak et l'identifiant de cette application.</summary>
public sealed record OidcSettings(Uri Authority, string ClientId, string Scope = "openid profile");

/// <summary>Une session ouverte : les jetons, gardés en mémoire seulement.</summary>
public sealed record OidcSession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string UserName, IReadOnlyList<string> Roles)
{
    public bool IsLlmAdmin => Roles.Contains("llm-admin");
}

/// <summary>PKCE (RFC 7636) : prouve que celui qui échange le code est celui qui l'a demandé.</summary>
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Connexion d'une application native (RFC 8252) : code d'autorisation + PKCE, dans le navigateur du système,
/// avec une redirection vers un port libre de la boucle locale. L'application ne voit jamais le mot de passe.
/// </summary>
public sealed class OidcClient(HttpClient backchannel, OidcSettings settings)
{
    private Discovery? _discovery;

    /// <param name="identityProvider">Alias d'un fournisseur du realm (« google », « github ») : Keycloak y envoie directement (kc_idp_hint).</param>
    public async Task<OidcSession> LoginAsync(Func<Uri, Task> openBrowser, string? identityProvider = null, CancellationToken cancellationToken = default)
    {
        var discovery = await DiscoverAsync(cancellationToken);
        var verifier = Pkce.NewVerifier();
        var state = Pkce.Base64Url(RandomNumberGenerator.GetBytes(16));
        var port = FreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["response_type"] = "code",
            ["scope"] = settings.Scope,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge"] = Pkce.Challenge(verifier),
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrEmpty(identityProvider))
            parameters["kc_idp_hint"] = identityProvider;
        var query = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        await openBrowser(new Uri($"{discovery.AuthorizationEndpoint}?{query}"));

        // On attend que le navigateur revienne sur la boucle locale avec le code.
        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        var callback = context.Request.QueryString;
        var error = callback["error"];
        if (error is not null || callback["state"] != state || string.IsNullOrEmpty(callback["code"]))
        {
            await RespondAsync(context, false);
            if (error is not null)
                throw new InvalidOperationException($"Connexion refusée : {callback["error_description"] ?? error}");
            if (callback["state"] != state)
                throw new InvalidOperationException("Réponse de connexion inattendue (state différent) : connexion annulée.");
            throw new InvalidOperationException("Réponse de connexion sans code : connexion annulée.");
        }

        OidcSession session;
        try
        {
            session = await TokenAsync(discovery, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = settings.ClientId,
                ["code"] = callback["code"]!,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier,
            }, cancellationToken);
        }
        catch
        {
            await RespondAsync(context, false);
            throw;
        }
        // Ne ferme l'onglet et n'annonce la réussite qu'après l'échange effectif du jeton.
        await RespondAsync(context, true);
        return session;
    }

    public async Task<OidcSession> RefreshAsync(OidcSession session, CancellationToken cancellationToken = default) =>
        await TokenAsync(await DiscoverAsync(cancellationToken), new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = settings.ClientId,
            ["refresh_token"] = session.RefreshToken ?? throw new InvalidOperationException("Session sans jeton de rafraîchissement."),
        }, cancellationToken);

    private async Task<OidcSession> TokenAsync(Discovery discovery, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await backchannel.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Échange du jeton refusé ({(int)response.StatusCode}).");
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!;
        var claims = ReadClaims(token.AccessToken);
        return new OidcSession(
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            claims.TryGetProperty("preferred_username", out var name) ? name.GetString() ?? "?" : "?",
            claims.TryGetProperty("realm_access", out var access) && access.TryGetProperty("roles", out var roles)
                ? [.. roles.EnumerateArray().Select(r => r.GetString() ?? "")]
                : []);
    }

    // Le contenu du jeton sert seulement à l'affichage (nom, rôles) : c'est l'API qui vérifie sa signature.
    private static JsonElement ReadClaims(string jwt)
    {
        var payload = jwt.Split('.') is [_, var middle, ..] ? middle.Replace('-', '+').Replace('_', '/') : "";
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    private async Task<Discovery> DiscoverAsync(CancellationToken cancellationToken) =>
        _discovery ??= await backchannel.GetFromJsonAsync<Discovery>(
            new Uri($"{settings.Authority.ToString().TrimEnd('/')}/.well-known/openid-configuration"), cancellationToken)
        ?? throw new InvalidOperationException("Document de découverte OIDC illisible.");

    private static int FreeLoopbackPort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static async Task RespondAsync(HttpListenerContext context, bool ok)
    {
        var title = ok ? "Connexion réussie" : "Connexion échouée";
        var message = ok ? "Vous êtes connecté. Retour à Agent Yattus…" : "Revenez à Agent Yattus pour réessayer.";
        // Certains navigateurs refusent de fermer un onglet qu'ils n'ont pas ouvert par script.
        var script = ok ? "<script>history.replaceState(null, '', '/'); setTimeout(() => window.close(), 300);</script>" : "";
        var fallback = ok ? "<p>Si cet onglet reste ouvert, vous pouvez le fermer. Le logiciel est prêt.</p>" : "";
        var html = $$"""
            <!doctype html><html lang="fr"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1"><title>{{title}} · Agent Yattus</title>
            <style>body{font-family:system-ui,sans-serif;background:#0E0F11;color:#ECE6D8;display:grid;place-items:center;min-height:100vh;margin:0}main{max-width:440px;padding:32px}h1{font-size:26px}p{line-height:1.6;color:#BDB8AC}</style>
            </head><body><main><h1>{{title}}</h1><p>{{message}}</p>{{fallback}}</main>{{script}}</body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private sealed record Discovery(
        [property: JsonPropertyName("authorization_endpoint")] Uri AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] Uri TokenEndpoint);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>
/// La session courante, partagée par le client HTTP et le hub. Rafraîchit le jeton juste avant son expiration.
/// </summary>
// ponytail: jetons en mémoire seulement, il faut se reconnecter à chaque lancement ;
// les garder dans le coffre du système (Trousseau, Gestionnaire d'identification) quand ce sera gênant.
public sealed class TokenStore(OidcClient oidc)
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly Lock _expire = new();

    public OidcSession? Session { get; set; }

    /// <summary>La session vient d'être perdue (rafraîchissement refusé, ou 401 de l'API) : il faut se reconnecter.
    /// Levé hors du thread UI.</summary>
    public event Action? Expired;

    /// <summary>Oublie la session ; prévient une seule fois, même si plusieurs requêtes échouent ensemble.</summary>
    public void Expire()
    {
        lock (_expire)
        {
            if (Session is null)
                return;
            Session = null;
        }
        Expired?.Invoke();
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Session is not { } session)
            return null;
        if (session.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            return session.AccessToken;

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (Session is { } current && current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return current.AccessToken;
            Session = await oidc.RefreshAsync(session, cancellationToken);
            return Session.AccessToken;
        }
        catch (InvalidOperationException)
        {
            // Rafraîchissement refusé (session expirée côté Keycloak) : il faudra se reconnecter.
            Expire();
            return null;
        }
        finally
        {
            _refresh.Release();
        }
    }
}

/// <summary>Ajoute « Authorization: Bearer … » à chaque requête vers l'API.</summary>
public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (await tokens.GetAccessTokenAsync(cancellationToken) is { } token)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        // L'API refuse le jeton (session fermée, révoquée) : retour à l'écran de connexion plutôt qu'une erreur brute.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            tokens.Expire();
        return response;
    }
}
