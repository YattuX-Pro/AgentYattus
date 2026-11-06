using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>L'écran de connexion : un bouton par moyen de connexion, le navigateur s'occupe du reste.</summary>
public sealed partial class LoginViewModel(OidcClient oidc, TokenStore tokens, Func<Uri, Task> openBrowser) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public event Action<OidcSession>? LoggedIn;

    private bool CanLogin => !IsBusy;

    /// <param name="provider">null : le formulaire Keycloak ; « google » ou « github » : directement chez ce fournisseur.</param>
    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(string? provider, CancellationToken cancellationToken)
    {
        IsBusy = true;
        Error = null;
        try
        {
            var session = await oidc.LoginAsync(openBrowser, provider, cancellationToken);
            tokens.Session = session;
            LoggedIn?.Invoke(session);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TimeoutException
                                   or System.Net.HttpListenerException or System.Text.Json.JsonException)
        {
            Error = ex is TimeoutException ? "Connexion abandonnée : aucune réponse du navigateur en 5 minutes." : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ponytail: la session reste ouverte côté Keycloak (SSO) ; appeler end_session_endpoint pour une vraie déconnexion.
    public void Logout() => tokens.Session = null;
}
