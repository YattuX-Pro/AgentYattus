# Leçon 30 — L'authentification avec Keycloak : protéger l'API

> **Commit** : `feat(auth): Keycloak et API protégée par jeton`
> **Phase** : authentification et gestion du LLM
> **Fichiers** : `docker-compose.yml`, `deploy/keycloak/`, `src/AgentYattus.Api/` (`Authentication.cs`, `Program.cs`, `appsettings.json`, `.csproj`), `docs/adr/0005-authentification-keycloak.md`, tests (`AuthTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

Jusqu'ici, n'importe quel programme de ta machine pouvait piloter les agents. Désormais :

- l'API exige un **jeton** sur chaque route, sauf `/health` ;
- les jetons sont émis par **Keycloak**, après connexion ;
- certaines actions (gérer les clés d'API, commit 32) exigent en plus un **rôle**.

## 2. Les notions

| Mot | Sens |
|---|---|
| **Authentification** | Prouver **qui** on est |
| **Autorisation** | Décider **ce qu'on a le droit** de faire |
| **Fournisseur d'identité** (IdP) | Le service qui vérifie l'identité et délivre les jetons : ici, Keycloak |
| **OpenID Connect** (OIDC) | Le protocole standard entre une application et un fournisseur d'identité |
| **Jeton d'accès** (JWT) | Un badge signé, à durée courte, que l'application présente à l'API |
| **Realm** | Un espace Keycloak : ses utilisateurs, ses rôles, ses applications |

> **Une image** : Keycloak est l'accueil d'un immeuble. Tu montres ta pièce d'identité une fois, l'accueil te donne un **badge** (le jeton) valable quelques minutes, pour certains étages (l'audience), avec certains droits (les rôles). L'API, elle, ne voit jamais ta pièce d'identité : elle vérifie seulement que le badge est authentique, pour elle, et pas expiré.

## 3. Keycloak en développement

```yaml
keycloak:
  image: quay.io/keycloak/keycloak:26.8.1
  command: ["start-dev", "--import-realm", "--http-port=8380"]
  volumes:
    - ./deploy/keycloak/agent-yattus-realm.json:/opt/keycloak/data/import/agent-yattus-realm.json:ro
  ports:
    - "127.0.0.1:8380:8380"
```

Au premier démarrage, Keycloak importe le realm `agent-yattus` :

- un rôle `llm-admin` ;
- une application `agent-yattus-desktop` : **client public** (pas de secret, une application installée ne peut pas en garder un), **code d'autorisation avec PKCE obligatoire**, redirection vers `http://127.0.0.1/*` ;
- un **mappeur d'audience** qui ajoute `agent-yattus-api` aux jetons ;
- deux comptes de test, `dev` (avec `llm-admin`) et `observateur` (sans), réservés au développement.

## 4. L'API vérifie les jetons — `Authentication.cs`

```csharp
.AddJwtBearer(options =>
{
    options.Authority = configuration["Auth:Authority"];   // http://localhost:8380/realms/agent-yattus
    options.Audience = configuration["Auth:Audience"];     // agent-yattus-api
    options.RequireHttpsMetadata = !environment.IsDevelopment();
```

À partir de l'`Authority`, l'API télécharge les **clés publiques** de Keycloak, et vérifie pour chaque jeton :

1. la **signature** : le jeton a bien été émis par Keycloak, et pas modifié ;
2. l'**émetteur** : le bon realm ;
3. l'**audience** : il a été émis **pour cette API**, et non pour une autre application ;
4. l'**expiration**.

L'API **ne parle jamais** à Keycloak pendant une requête : elle vérifie la signature avec la clé publique, en local. C'est rapide, et ça marche même si Keycloak est momentanément indisponible.

### Tout est protégé par défaut

```csharp
.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
```

```csharp
app.MapHealthChecks("/health").AllowAnonymous();
```

La **politique par défaut** s'applique à toutes les routes. Une nouvelle route est donc protégée **d'office** ; c'est l'exception (`/health`) qu'il faut déclarer. Oublier de protéger une route devient impossible.

### Les rôles de Keycloak

```csharp
// « realm_access »: { "roles": [...] }  →  une revendication de rôle par rôle
foreach (var role in roles.EnumerateArray())
    identity.AddClaim(new Claim(ClaimTypes.Role, name));
```

Keycloak range les rôles dans un objet JSON ; ASP.NET Core attend une revendication par rôle. Cette petite traduction permet ensuite d'écrire `policy.RequireRole("llm-admin")`.

### Le hub SignalR

```csharp
OnMessageReceived = context =>
{
    if (context.Request.Path.StartsWithSegments(MissionsHubProtocol.Path)
        && context.Request.Query["access_token"] is [{ Length: > 0 } token])
        context.Token = token;
```

Un client WebSocket ne peut pas envoyer d'en-tête `Authorization`. Le client SignalR passe donc le jeton dans l'URL, et l'API ne l'accepte ainsi **que** pour le hub. Le jeton dure 5 minutes, ce qui limite le risque s'il apparaît dans un journal.

## 5. Tester sans Keycloak

- **Les tests existants** utilisent un schéma d'authentification de test qui connecte d'office un utilisateur fictif : ils restent centrés sur leur sujet.
- **`AuthTests`** vérifie la **vraie** validation : l'API reçoit une clé de signature de test à la place de celle de Keycloak, et les tests fabriquent des jetons.

| Test | Vérifie |
|---|---|
| `Health_is_public` | `/health` sans jeton : 200 |
| `Everything_else_requires_a_token` | `/missions`, `/agents`, le hub : 401 sans jeton |
| `A_valid_token_for_the_api_is_accepted` | Un jeton valide : 200 |
| `A_token_for_another_audience_an_expired_token_or_a_forged_signature_is_refused` | Mauvaise audience, jeton expiré, signature forgée : 401 |
| `Keycloak_realm_roles_become_role_claims` | La traduction des rôles |

### Et avec le vrai Keycloak

Vérifié aussi de bout en bout : Keycloak démarré, un script a joué le parcours du navigateur (formulaire de connexion, redirection vers `127.0.0.1` avec un code, échange du code avec PKCE). Puis :

| Appel à l'API réelle | Résultat |
|---|---|
| `/health` sans jeton | 200 |
| `/missions` sans jeton | 401 |
| `/missions` avec le jeton Keycloak | 200 |
| `/missions` avec un jeton altéré | 401 |
| Hub avec `access_token` / sans | 200 / 401 |

Le jeton contenait bien l'audience `agent-yattus-api`, et le rôle `llm-admin` pour `dev` seulement.

## 6. Ce qui ne marche plus… pour l'instant

Le desktop n'envoie pas encore de jeton : il reçoit des 401. La connexion depuis le desktop est le commit suivant.

## 7. Vérifie toi-même

```bash
docker compose up -d keycloak
```

Ouvre <http://localhost:8380/realms/agent-yattus/.well-known/openid-configuration> : c'est le **document de découverte** OIDC, où l'API trouve tout ce dont elle a besoin.

```bash
dotnet run --project tests/AgentYattus.Api.Tests -- -class "AgentYattus.Api.Tests.AuthTests"
```

## 8. Quiz

1. Quelle différence entre authentification et autorisation ?
2. Quelles sont les quatre vérifications que l'API fait sur chaque jeton ?
3. Pourquoi l'API n'appelle-t-elle pas Keycloak à chaque requête ?
4. Pourquoi une politique par défaut plutôt que protéger chaque route une par une ?
5. Pourquoi le desktop est-il un client « public », sans secret ?

## 9. Pour aller plus loin

- L'ADR : `docs/adr/0005-authentification-keycloak.md`
- JWT dans ASP.NET Core : <https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- SignalR et authentification : <https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz>
- Keycloak : <https://www.keycloak.org/documentation>

<details>
<summary>Réponses du quiz</summary>

1. L'authentification prouve qui on est ; l'autorisation décide ce qu'on a le droit de faire.
2. La signature, l'émetteur, l'audience et l'expiration.
3. Parce qu'elle vérifie la signature localement avec la clé publique de Keycloak, téléchargée une fois.
4. Pour qu'une nouvelle route soit protégée d'office : l'oubli devient impossible, seule l'exception se déclare.
5. Parce qu'une application installée chez l'utilisateur ne peut pas garder un secret : n'importe qui pourrait l'extraire.

</details>
