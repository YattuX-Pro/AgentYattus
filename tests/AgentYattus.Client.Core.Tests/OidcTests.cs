using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AgentYattus.Client.Core.Tests;

/// <summary>La connexion OIDC, avec un faux Keycloak (HTTP simulé) et un faux navigateur qui revient sur la boucle locale.</summary>
public sealed class OidcTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly OidcSettings Settings = new(new Uri("http://keycloak.test/realms/agent-yattus"), "agent-yattus-desktop");

    [Fact]
    public void Pkce_matches_the_rfc_7636_example()
    {
        // RFC 7636, annexe B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.Equal(43, Pkce.NewVerifier().Length);
    }

    [Fact]
    public async Task Login_opens_the_browser_and_exchanges_the_code_with_the_pkce_verifier()
    {
        var keycloak = new FakeKeycloak();
        var oidc = new OidcClient(new HttpClient(keycloak), Settings);
        Uri? authorizeUrl = null;

        var session = await oidc.LoginAsync(url =>
        {
            authorizeUrl = url;
            return Browser.ReturnAsync(url, code: "code-123");
        }, cancellationToken: Ct);

        var authorize = HttpUtility.ParseQueryString(authorizeUrl!.Query);
        Assert.Equal("S256", authorize["code_challenge_method"]);
        Assert.StartsWith("http://127.0.0.1:", authorize["redirect_uri"]);
        var exchange = HttpUtility.ParseQueryString(keycloak.LastTokenForm!);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("code-123", exchange["code"]);
        // Le vérificateur envoyé à l'échange correspond au défi envoyé au départ : c'est tout l'intérêt de PKCE.
        Assert.Equal(authorize["code_challenge"], Pkce.Challenge(exchange["code_verifier"]!));
        Assert.Equal("dev", session.UserName);
        Assert.True(session.IsLlmAdmin);
    }

    [Fact]
    public async Task Login_with_a_provider_asks_keycloak_to_go_straight_to_it()
    {
        var oidc = new OidcClient(new HttpClient(new FakeKeycloak()), Settings);
        Uri? withProvider = null, without = null;

        await oidc.LoginAsync(url => { withProvider = url; return Browser.ReturnAsync(url, code: "c"); }, "github", Ct);
        await oidc.LoginAsync(url => { without = url; return Browser.ReturnAsync(url, code: "c"); }, cancellationToken: Ct);

        Assert.Equal("github", HttpUtility.ParseQueryString(withProvider!.Query)["kc_idp_hint"]);
        Assert.Null(HttpUtility.ParseQueryString(without!.Query)["kc_idp_hint"]);
    }

    [Fact]
    public async Task A_response_with_another_state_is_rejected()
    {
        var oidc = new OidcClient(new HttpClient(new FakeKeycloak()), Settings);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            oidc.LoginAsync(url => Browser.ReturnAsync(url, code: "code-123", state: "forgé"), cancellationToken: Ct));

        Assert.Contains("state", error.Message);
    }

    [Fact]
    public async Task The_token_is_refreshed_just_before_it_expires_and_sent_as_a_bearer()
    {
        var keycloak = new FakeKeycloak();
        var tokens = new TokenStore(new OidcClient(new HttpClient(keycloak), Settings))
        {
            Session = new OidcSession("ancien", "refresh-1", DateTimeOffset.UtcNow.AddSeconds(10), "dev", []),
        };
        string? sent = null;
        var api = new HttpClient(new BearerTokenHandler(tokens)
        {
            InnerHandler = new Stub(request =>
            {
                sent = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
        });

        await api.GetAsync("http://api/missions", Ct);

        Assert.Equal("refresh_token", HttpUtility.ParseQueryString(keycloak.LastTokenForm!)["grant_type"]);
        Assert.Equal($"Bearer {FakeKeycloak.AccessToken}", sent);
    }

    [Fact]
    public async Task Logging_in_shows_home_and_logging_out_shows_the_login_screen()
    {
        var oidc = new OidcClient(new HttpClient(new FakeKeycloak()), Settings);
        var tokens = new TokenStore(oidc);
        var api = new ApiClient(new HttpClient(new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK))) { BaseAddress = new Uri("http://api/") });
        var home = new HomeViewModel(api);
        var login = new LoginViewModel(oidc, tokens, url => Browser.ReturnAsync(url, code: "code-123"));
        var shell = new ShellViewModel(api, home, login);
        Assert.Same(login, shell.Current);
        Assert.False(shell.IsAuthenticated);

        await login.LoginCommand.ExecuteAsync(null);

        Assert.Same(home, shell.Current);
        Assert.Equal("dev", shell.UserName);
        Assert.True(shell.IsLlmAdmin);
        Assert.NotNull(tokens.Session);

        shell.LogoutCommand.Execute(null);

        Assert.Same(login, shell.Current);
        Assert.Null(tokens.Session);
    }

    /// <summary>Le navigateur après la connexion : il revient sur l'adresse de redirection, avec le code.</summary>
    private static class Browser
    {
        public static Task ReturnAsync(Uri authorizeUrl, string code, string? state = null)
        {
            var query = HttpUtility.ParseQueryString(authorizeUrl.Query);
            var callback = $"{query["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(state ?? query["state"]!)}";
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                await http.GetAsync(callback);
            });
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeycloak : HttpMessageHandler
    {
        public static readonly string AccessToken = Jwt(new { preferred_username = "dev", realm_access = new { roles = new[] { "llm-admin" } } });

        public string? LastTokenForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("openid-configuration"))
                return Json(new
                {
                    authorization_endpoint = "http://keycloak.test/realms/agent-yattus/protocol/openid-connect/auth",
                    token_endpoint = "http://keycloak.test/realms/agent-yattus/protocol/openid-connect/token",
                });
            LastTokenForm = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(new { access_token = AccessToken, refresh_token = "refresh-2", expires_in = 300 });
        }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        private static string Jwt(object payload) =>
            $"{Pkce.Base64Url("{\"alg\":\"none\"}"u8.ToArray())}.{Pkce.Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}.signature";
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
