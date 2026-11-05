# ADR 0005 — Authentification : Keycloak et jetons JWT

- **Statut** : proposé
- **Date** : 2026-11-05

## Contexte

L'API pilote des agents qui lisent et écrivent sur le disque, et va bientôt gérer des clés d'API payantes. Jusqu'ici, elle n'était protégée que par son écoute sur la boucle locale (ADR 0004). Pour le mode serveur, le mobile, et la gestion des clés, il faut savoir **qui** appelle, et **ce qu'il a le droit de faire**.

## Décision

1. **Keycloak** comme fournisseur d'identité (OpenID Connect), lancé par Docker Compose en développement, avec un realm importé (`deploy/keycloak/agent-yattus-realm.json`).
2. **L'API valide des jetons JWT** (`Microsoft.AspNetCore.Authentication.JwtBearer`) :
   - émetteur : le realm `agent-yattus` ;
   - audience : `agent-yattus-api` (un jeton émis pour une autre application est refusé) ;
   - toutes les routes exigent un utilisateur connecté (politique par défaut), sauf `/health` ;
   - le hub SignalR reçoit le jeton dans l'URL (`access_token`), seule façon pour un client WebSocket de le transmettre ;
   - les rôles du realm (`realm_access.roles`) deviennent des rôles ASP.NET Core ; la gestion du LLM exige le rôle `llm-admin`.
3. **Les applications natives** (desktop, puis mobile) sont des **clients publics** : code d'autorisation avec **PKCE**, connexion dans le navigateur du système, redirection vers la boucle locale `http://127.0.0.1:<port>/` (RFC 8252). Pas de mot de passe saisi dans l'application, pas de secret client embarqué.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| ASP.NET Core Identity dans l'API | Pas de service à part | L'API gère mots de passe, réinitialisation, MFA : beaucoup de code sensible à maintenir |
| Un service hébergé (Entra ID, Auth0) | Rien à opérer | Dépendance à un compte externe, même en développement local |
| **Keycloak** | Open source, standard OIDC, rôles, MFA, fonctionne hors ligne en local | Un conteneur de plus ; configuration du realm à apprendre |
| Mot de passe saisi dans l'application (*password grant*) | Simple | Déconseillé (OAuth 2.0 Security BCP) : l'application voit le mot de passe ; pas de MFA |

## Conséquences

- (+) L'API sait qui agit : on pourra tracer qui a approuvé quoi, et réserver la gestion des clés aux administrateurs.
- (+) Changer de fournisseur (Entra ID, Auth0…) ne demande qu'une configuration : l'API ne connaît que OIDC et JWT.
- (−) Lancer l'application demande Keycloak (`docker compose up`).
- (−) Le realm de développement contient des comptes de test aux mots de passe connus : il ne doit jamais être déployé tel quel.
- (−) Le jeton dans l'URL du hub peut apparaître dans des journaux d'accès : jeton à durée courte (5 minutes), et à exclure des journaux en production.
- Le mode local sans serveur (ADR 0004) gardera un jeton par lancement plutôt que Keycloak.

## Sources

- OAuth 2.0 pour les applications natives (RFC 8252) : <https://www.rfc-editor.org/rfc/rfc8252>
- PKCE (RFC 7636) : <https://www.rfc-editor.org/rfc/rfc7636>
- Authentification JWT dans ASP.NET Core : <https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- SignalR, authentification : <https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz>
- Keycloak, import d'un realm : <https://www.keycloak.org/server/importExport>
