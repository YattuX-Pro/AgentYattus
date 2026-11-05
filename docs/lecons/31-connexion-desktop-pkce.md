# Leçon 31 — Se connecter depuis le desktop : OAuth 2.0 avec PKCE

> **Commit** : `feat(desktop): connexion OpenID Connect avec PKCE`
> **Phase** : authentification et gestion du LLM
> **Fichiers** : `src/AgentYattus.Client.Core/` (`Oidc.cs`, `LoginViewModel.cs`, `ShellViewModel.cs`, `MissionsHubClient.cs`), `src/AgentYattus.Desktop/` (`App.axaml.cs`, `MainWindow.axaml`, `Views/LoginView.axaml`), `tests/AgentYattus.Client.Core.Tests/OidcTests.cs`

## 1. Ce qu'on construit

Au lancement, le desktop affiche une seule carte : **« Se connecter »**. Un clic ouvre ton **navigateur** sur la page de Keycloak ; tu t'y connectes ; le navigateur revient à l'application, qui affiche l'accueil avec ton nom en haut à droite.

**L'application ne voit jamais ton mot de passe.**

## 2. Pourquoi le navigateur, et pas un formulaire dans l'application ?

| Formulaire dans l'application | Navigateur du système |
|---|---|
| L'application voit le mot de passe | Seul Keycloak le voit |
| Pas de gestionnaire de mots de passe | Ton gestionnaire, tes clés de sécurité, ta MFA |
| Chaque application refait l'écran | Une seule page de connexion, déjà connectée si tu l'étais (SSO) |
| Déconseillé par les standards | Recommandé : RFC 8252, « OAuth 2.0 pour les applications natives » |

## 3. Le parcours, étape par étape

```
 Desktop                                Navigateur              Keycloak
   │ 1. prépare verifier + challenge
   │ 2. écoute sur 127.0.0.1:<port libre>
   │ 3. ouvre ──────────────────────────► /auth?…&code_challenge=…&state=…
   │                                         │ 4. tu te connectes ───► vérifie
   │                                         │ ◄─────────── 5. redirige vers 127.0.0.1:<port>/?code=…&state=…
   │ ◄──────────── 6. le navigateur revient sur l'écoute locale
   │ 7. vérifie le state
   │ 8. POST /token  code + code_verifier ─────────────────────────────────► vérifie challenge = SHA256(verifier)
   │ ◄───────────────────────────────────────── 9. jeton d'accès + jeton de rafraîchissement
```

### PKCE : la preuve que c'est bien toi qui échanges le code

Le **code** d'autorisation transite par le navigateur ; un programme malveillant pourrait l'intercepter. **PKCE** (*Proof Key for Code Exchange*, RFC 7636) l'empêche de s'en servir :

```csharp
public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));
public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
```

1. L'application tire un **secret aléatoire** (le *verifier*), et n'envoie au départ que son **empreinte** SHA-256 (le *challenge*).
2. À l'échange, elle envoie le secret lui-même. Keycloak vérifie que son empreinte correspond.

Un intercepteur a le code, mais pas le secret : l'échange échoue.

### Le `state` : la réponse correspond-elle à notre demande ?

```csharp
if (callback["state"] != state)
    throw new InvalidOperationException("Réponse de connexion inattendue (state différent) : connexion annulée.");
```

Une valeur aléatoire envoyée au départ et vérifiée au retour. Elle empêche qu'une réponse fabriquée ailleurs (attaque *CSRF*) ou un vieil onglet ne connecte l'application avec le mauvais compte.

### La redirection vers la boucle locale

```csharp
var port = FreeLoopbackPort();
var redirectUri = $"http://127.0.0.1:{port}/";
using var listener = new HttpListener();
listener.Prefixes.Add(redirectUri);
```

L'application ouvre un mini-serveur web sur un **port libre** de `127.0.0.1`, le temps d'une connexion. Keycloak accepte n'importe quel port pour l'adresse `http://127.0.0.1/*` déclarée dans le realm : c'est prévu par la RFC 8252, et on l'a vérifié avec deux ports différents.

## 4. Après la connexion

### Les jetons

```csharp
public sealed record OidcSession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string UserName, IReadOnlyList<string> Roles)
```

- Le **jeton d'accès** dure 5 minutes.
- Le **jeton de rafraîchissement** permet d'en obtenir un nouveau sans te redemander de te connecter :

```csharp
if (session.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
    return session.AccessToken;
...
Session = await oidc.RefreshAsync(session, cancellationToken);
```

`TokenStore` rafraîchit **30 secondes avant** l'expiration, et un `SemaphoreSlim` évite que plusieurs requêtes simultanées rafraîchissent en même temps.

```csharp
// Le contenu du jeton sert seulement à l'affichage (nom, rôles) : c'est l'API qui vérifie sa signature.
```

L'application lit le nom et les rôles dans le jeton pour l'**affichage**. Elle ne vérifie pas sa signature : ce n'est pas elle qui protège quoi que ce soit, c'est l'API.

### Chaque requête porte le jeton

```csharp
public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
```

Un **maillon** inséré dans le `HttpClient` : il ajoute `Authorization: Bearer …` à chaque requête. `ApiClient` n'a pas changé d'une ligne.

Le hub reçoit le jeton par `AccessTokenProvider`. L'application ne s'y connecte qu'**après** la connexion, et s'en détache à la déconnexion.

### Ce qui reste simple, volontairement

- **Jetons en mémoire** : il faut se reconnecter à chaque lancement. Les garder dans le coffre du système (Trousseau macOS, Gestionnaire d'identification Windows) viendra quand ce sera gênant.
- **Déconnexion locale** : l'application oublie les jetons, mais la session Keycloak reste ouverte dans le navigateur.

Tout ça sans nouvelle dépendance : `HttpListener`, `SHA256`, `RandomNumberGenerator` et `HttpClient` sont dans .NET.

## 5. Les tests

| Test | Vérifie |
|---|---|
| `Pkce_matches_the_rfc_7636_example` | Le calcul du challenge, sur l'exemple officiel de la RFC |
| `Login_opens_the_browser_and_exchanges_the_code_with_the_pkce_verifier` | Le parcours complet, avec un faux Keycloak et un faux navigateur qui revient **réellement** sur l'écoute locale ; le verifier échangé correspond au challenge envoyé |
| `A_response_with_another_state_is_rejected` | Un `state` forgé est refusé |
| `The_token_is_refreshed_just_before_it_expires_and_sent_as_a_bearer` | Rafraîchissement, et en-tête `Bearer` |
| `Logging_in_shows_home_and_logging_out_shows_the_login_screen` | La coquille bascule à la connexion et à la déconnexion |

**Et contre le vrai Keycloak** : le vrai `OidcClient` a été lancé contre le Keycloak du Docker Compose, avec un « navigateur » scripté qui remplit le formulaire. Keycloak a redirigé vers un port libre de `127.0.0.1`, le code a été échangé, les rôles lus (`llm-admin` pour `dev`, aucun pour `observateur`), et le rafraîchissement a rendu un nouveau jeton.

## 6. Vérifie toi-même

```bash
docker compose up -d
```

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

```bash
dotnet run --project src/AgentYattus.Desktop
```

Clique « Se connecter », puis connecte-toi avec `dev` / `agent-yattus-dev` (compte de développement, voir `deploy/keycloak/README.md`).

## 7. Quiz

1. Pourquoi se connecter dans le navigateur plutôt que dans l'application ?
2. Que protège PKCE, et comment ?
3. À quoi sert le `state` ?
4. Pourquoi l'application peut-elle lire le jeton sans vérifier sa signature ?
5. Que fait `BearerTokenHandler`, et pourquoi `ApiClient` n'a-t-il pas changé ?

## 8. Pour aller plus loin

- RFC 8252, OAuth 2.0 pour les applications natives : <https://www.rfc-editor.org/rfc/rfc8252>
- RFC 7636, PKCE : <https://www.rfc-editor.org/rfc/rfc7636>
- OAuth 2.0 Security Best Current Practice : <https://www.rfc-editor.org/rfc/rfc9700>

<details>
<summary>Réponses du quiz</summary>

1. L'application ne voit jamais le mot de passe, et l'utilisateur garde son gestionnaire de mots de passe, sa MFA et sa session existante.
2. L'usage d'un code intercepté : l'application envoie d'abord l'empreinte d'un secret, puis le secret à l'échange ; sans le secret, le code ne sert à rien.
3. À vérifier que la réponse correspond bien à notre demande (protection contre le CSRF et les onglets périmés).
4. Parce qu'elle ne s'en sert que pour l'affichage ; c'est l'API qui protège, et elle vérifie la signature.
5. Il ajoute l'en-tête `Authorization: Bearer` à chaque requête ; c'est un maillon du `HttpClient`, donc transparent pour `ApiClient`.

</details>
