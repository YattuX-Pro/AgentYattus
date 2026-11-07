# Leçon 127 — Une session expirée ramène à la connexion

> **Commit** : `fix(desktop): une session expirée ramène à l'écran de connexion`
> **Phase** : C (correctif)
> **Fichiers** : `Client.Core/{Oidc,ShellViewModel}.cs`, `Desktop/App.axaml.cs`, `tests/Client.Core.Tests/OidcTests.cs`

## 1. Le symptôme

Le logiciel est resté ouvert plusieurs heures. À l'envoi d'une mission, un message rouge et technique apparaît : `Response status code does not indicate success: 401 (Unauthorized)`.

## 2. La cause

Keycloak donne deux jetons :

| Jeton | Durée (realm `agent-yattus`) | Rôle |
|---|---|---|
| d'accès | 5 minutes | envoyé à l'API à chaque requête |
| de rafraîchissement | 30 minutes **sans activité** | redemande un jeton d'accès |

Après 30 minutes sans rien faire, Keycloak ferme la session. Le rafraîchissement est refusé. `TokenStore` oubliait alors la session **sans prévenir personne** : le logiciel envoyait ses requêtes sans jeton, l'API répondait 401, et l'écran affichait l'erreur brute.

Pour le trouver, on a d'abord prouvé que le serveur allait bien. Les journaux d'authentification de l'API (`Microsoft.AspNetCore.Authentication: Debug`) donnent la raison de chaque refus. Une vraie connexion avec le compte de test était acceptée, et les 401 venaient de l'ancienne session.

## 3. La correction

- `TokenStore.Expire()` oublie la session et lève l'événement `Expired`, **une seule fois** même si plusieurs requêtes échouent en même temps (un verrou, et le test de `Session is null`).
- Il est appelé dans deux cas :
  - le rafraîchissement est refusé ;
  - l'API répond 401 (`BearerTokenHandler`), par exemple pour une session révoquée par un administrateur.
- `ShellViewModel.SessionExpired()` revient à l'écran de connexion, avec la raison : « Votre session a expiré : reconnectez-vous. »
- L'événement arrive depuis une requête HTTP, hors du thread de l'interface. L'application le repasse donc au thread UI (`Dispatcher.UIThread.Post`) avant de toucher l'écran.

## 4. Vérifie toi-même

`dotnet test tests/AgentYattus.Client.Core.Tests --filter A_session_closed_by_keycloak`. Ou bien : lance le logiciel, connecte-toi, attends 30 minutes, puis clique quelque part. L'écran de connexion revient.

## 5. Les limites

- Ce qu'on était en train d'écrire est perdu au retour à la connexion. On pourrait le garder si cela gêne.
- Les jetons restent en mémoire (leçon sur `TokenStore`) : fermer le logiciel oblige aussi à se reconnecter.

## 6. Quiz

1. Pourquoi ne pas simplement allonger la durée des jetons dans Keycloak ?
2. Pourquoi l'événement ne doit-il être levé qu'une fois ?

<details>
<summary>Réponses</summary>

1. Un jeton volé resterait utilisable plus longtemps. Des jetons courts plus un retour propre à la connexion valent mieux qu'un jeton long.
2. Plusieurs écrans chargent leurs données en parallèle. Chaque requête refusée relancerait la déconnexion, et l'écran clignoterait ou perdrait le message.

</details>
