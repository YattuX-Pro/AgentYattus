using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgentYattus.Api.Tests;

/// <summary>
/// La vraie validation des jetons, sans Keycloak : les jetons de test sont signés avec une clé locale,
/// que l'API reçoit à la place de celle publiée par Keycloak.
/// </summary>
public sealed class AuthTests(AuthTests.JwtApiFactory factory) : IClassFixture<AuthTests.JwtApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/missions")]
    [InlineData("/agents")]
    [InlineData("/hubs/missions/negotiate?negotiateVersion=1")]
    public async Task Everything_else_requires_a_token(string path)
    {
        var request = new HttpRequestMessage(path.Contains("negotiate") ? HttpMethod.Post : HttpMethod.Get, path);

        var response = await factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_for_the_api_is_accepted()
    {
        var response = await GetMissionsAsync(JwtApiFactory.Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_an_expired_token_or_a_forged_signature_is_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMissionsAsync(JwtApiFactory.Token(audience: "autre-api"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMissionsAsync(JwtApiFactory.Token(expires: DateTime.UtcNow.AddMinutes(-10)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMissionsAsync(JwtApiFactory.Token(key: new string('x', 64)))).StatusCode);
    }

    [Fact]
    public void Keycloak_realm_roles_become_role_claims()
    {
        var identity = new ClaimsIdentity([new Claim("realm_access", """{"roles":["llm-admin","offline_access"]}""", JsonClaimValueTypes.Json)]);

        Authentication.AddRealmRoles(new ClaimsPrincipal(identity));

        Assert.Equal(["llm-admin", "offline_access"], identity.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    private async Task<HttpResponseMessage> GetMissionsAsync(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/missions", Ct);
    }

    public sealed class JwtApiFactory : ApiFactory
    {
        private const string Issuer = "http://keycloak.test/realms/agent-yattus";
        private static readonly string SigningKey = new('k', 64);

        protected override void ConfigureAuthentication(IServiceCollection services) =>
            // Pas d'Authority : l'API n'ira pas chercher les clés de Keycloak, elle utilise la clé de test.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = "agent-yattus-api";
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
            });

        public static string Token(string audience = "agent-yattus-api", DateTime? expires = null, string? key = null) =>
            new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                NotBefore = (expires ?? DateTime.UtcNow.AddMinutes(5)).AddMinutes(-30),
                Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
                Claims = new Dictionary<string, object>
                {
                    ["preferred_username"] = "dev",
                    ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { "llm-admin" } },
                },
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? SigningKey)), SecurityAlgorithms.HmacSha256),
            });
    }
}
