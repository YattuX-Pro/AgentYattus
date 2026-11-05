using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AgentYattus.Api;

/// <summary>
/// L'API ne fait confiance qu'aux jetons émis par Keycloak pour elle (audience « agent-yattus-api »).
/// Tout est protégé, sauf /health ; gérer le LLM demande en plus le rôle « llm-admin ».
/// </summary>
internal static class Authentication
{
    public const string LlmAdminPolicy = "llm-admin";
    public const string LlmAdminRole = "llm-admin";

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Auth:Authority"];
                options.Audience = configuration["Auth:Audience"];
                // Keycloak de développement en HTTP sur la boucle locale ; HTTPS obligatoire partout ailleurs.
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                options.Events = new JwtBearerEvents
                {
                    // Un navigateur ou un client WebSocket ne peut pas envoyer d'en-tête Authorization au hub :
                    // le client SignalR passe le jeton dans l'URL, seulement pour le hub.
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments(Contracts.MissionsHubProtocol.Path)
                            && context.Request.Query["access_token"] is [{ Length: > 0 } token])
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        AddRealmRoles(context.Principal);
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            // Par défaut, chaque route exige un utilisateur connecté ; /health s'en exempte explicitement.
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(LlmAdminPolicy, policy => policy.RequireRole(LlmAdminRole));
        return services;
    }

    /// <summary>
    /// Keycloak range les rôles du realm dans un objet JSON (« realm_access »: { "roles": [...] }) ;
    /// ASP.NET Core attend une revendication par rôle.
    /// </summary>
    public static void AddRealmRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity
            || identity.FindFirst("realm_access")?.Value is not { } realmAccess)
            return;
        using var document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            return;
        foreach (var role in roles.EnumerateArray())
            if (role.GetString() is { Length: > 0 } name)
                identity.AddClaim(new Claim(ClaimTypes.Role, name));
    }
}
