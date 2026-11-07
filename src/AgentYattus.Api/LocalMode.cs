using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api;

/// <summary>
/// Le mode local (ADR 0014) : l'API lancée par le logiciel, sur la boucle locale, avec SQLite et sans Keycloak.
/// La seule porte d'entrée est le jeton tiré par le logiciel à chaque lancement.
/// </summary>
internal static class LocalMode
{
    public const string SchemeName = "Local";

    /// <summary>L'identifiant stable de la personne de la machine (la revendication « sub »).</summary>
    public const string UserId = "local";

    public static bool IsOn(IConfiguration configuration) =>
        string.Equals(configuration["Mode"], "Local", StringComparison.OrdinalIgnoreCase);

    /// <summary>Le dossier de données : configuré, sinon celui de l'utilisateur (Application Support, %LOCALAPPDATA%).</summary>
    public static string DataFolder(IConfiguration configuration)
    {
        var folder = configuration["Local:DataFolder"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentYattus");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string ConnectionString(IConfiguration configuration) =>
        $"Data Source={Path.Combine(DataFolder(configuration), "agent-yattus.db")}";

    public static IServiceCollection AddLocalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Sans jeton, l'API refuse de démarrer : jamais une API locale ouverte à tous les programmes de la machine.
        var token = configuration["Local:Token"];
        if (token is not { Length: >= 32 })
            throw new InvalidOperationException(
                "Mode local : AGENTYATTUS_LOCAL_TOKEN (Local__Token) doit contenir au moins 32 caractères, tirés au hasard par le logiciel.");
        services.AddAuthentication(SchemeName)
            .AddScheme<LocalTokenOptions, LocalTokenHandler>(SchemeName, options => options.Token = token);
        if (int.TryParse(configuration["Local:ParentPid"], out var parent))
            services.AddHostedService(sp => new ParentWatcher(parent, sp.GetRequiredService<IHostApplicationLifetime>()));
        return services;
    }
}

/// <summary>
/// Si le logiciel s'arrête sans prévenir (plantage, fin forcée), l'API locale le remarque et s'arrête aussi :
/// pas d'API orpheline qui tourne en arrière-plan.
/// </summary>
internal sealed class ParentWatcher(int parentPid, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsAlive(parentPid))
            {
                lifetime.StopApplication();
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed class LocalTokenOptions : AuthenticationSchemeOptions
{
    public string Token { get; set; } = "";
}

/// <summary>
/// « Authorization: Bearer &lt;jeton&gt; », ou « ?access_token= » pour le hub SignalR (comme avec Keycloak).
/// Comparé en temps constant : la durée de la réponse ne dit rien du jeton.
/// </summary>
internal sealed class LocalTokenHandler(IOptionsMonitor<LocalTokenOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<LocalTokenOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? presented = null;
        if (Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal))
            presented = header["Bearer ".Length..].Trim();
        else if (Request.Path.StartsWithSegments(Contracts.MissionsHubProtocol.Path) && Request.Query["access_token"] is [{ Length: > 0 } query])
            presented = query;
        if (presented is null)
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(Options.Token)))
            return Task.FromResult(AuthenticateResult.Fail("Jeton local invalide."));

        // La personne de la machine : seule utilisatrice, et administratrice de son instance.
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", LocalMode.UserId),
            new Claim("preferred_username", Environment.UserName is { Length: > 0 } name ? name : "moi"),
            new Claim(ClaimTypes.Role, Authentication.LlmAdminRole),
        ], LocalMode.SchemeName, "preferred_username", ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), LocalMode.SchemeName)));
    }
}
