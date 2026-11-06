# Leçon 39 — Se connecter à l'espace admin : Keycloak, PKCE et jetons en mémoire

> **Commit** : `feat(admin): connexion Keycloak (PKCE), garde admin et relais vers l'API`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/` (`src/app/core/auth.ts`, `auth.spec.ts`, `app.config.ts`, `app.routes.ts`, `pages/forbidden.ts`, `proxy.conf.json`, `angular.json`), `deploy/keycloak/` (`agent-yattus-realm.json`, `README.md`)

## 1. Ce qu'on construit

- Ouvrir `http://localhost:4200` envoie chez **Keycloak** ; après la connexion, on revient sur la page demandée.
- Un compte **sans le rôle administrateur** voit une page « Accès réservé aux administrateurs ».
- Chaque appel à `/api/…` part avec le jeton ; le serveur de dev d'Angular le **relaie** à l'API.

## 2. Le même flux que le bureau, dans un navigateur

La leçon 31 a codé le flux à la main pour le bureau. Dans un navigateur, on prend une bibliothèque éprouvée, **oidc-client-ts**, qui fait la même chose :

1. créer un secret aléatoire (le *code verifier*) et en envoyer l'empreinte à Keycloak ;
2. Keycloak authentifie, puis renvoie vers `http://localhost:4200/?code=…&state=…` ;
3. échanger le `code` contre des jetons, **avec le secret** : un code volé en route ne sert à rien sans lui. C'est **PKCE**.

Côté Keycloak, un nouveau client **public** : une page web ne peut garder aucun secret, puisque tout son code est lisible dans le navigateur.

```json
"clientId": "agent-yattus-admin",
"publicClient": true,
"redirectUris": ["http://localhost:4200/*"],
"webOrigins": ["http://localhost:4200"],
"attributes": { "pkce.code.challenge.method": "S256" }
```

`webOrigins` autorise la page à appeler Keycloak elle-même (l'échange du code), malgré la règle de même origine.

## 3. Où ranger les jetons ?

```ts
userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
stateStore: new WebStorageStateStore({ store: sessionStorage }),
```

| Donnée | Où | Pourquoi |
|---|---|---|
| Jetons | **en mémoire** | Un script injecté (XSS) pourrait lire `localStorage` et partir avec |
| Secret PKCE, le temps de l'aller-retour | `sessionStorage` | Il doit survivre à la redirection, et seulement dans cet onglet |

Recharger la page efface les jetons : on repasse par Keycloak, qui a encore sa **session** (son propre cookie) et renvoie aussitôt, sans mot de passe.

Le jeton d'accès vit 5 minutes. `automaticSilentRenew` le renouvelle avant l'échéance, avec le jeton de rafraîchissement.

## 4. Rien ne s'affiche avant la connexion

```ts
provideAppInitializer(() => inject(AuthService).init()),
```

Angular attend la promesse avant de démarrer. `init()` termine le retour de Keycloak, ou y part. Ainsi aucune page ne s'affiche sans utilisateur, et aucun composant n'a à gérer « pas encore connecté ».

⚠️ Revenir « sur la page demandée » est un piège classique : la *redirection ouverte*. On accepte seulement un chemin local :

```ts
const target = typeof user.state === 'string' && user.state.startsWith('/') ? user.state : '/';
```

## 5. Le jeton, seulement pour notre API

```ts
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) return next(request);
  ...
};
```

Un **intercepteur** voit passer chaque requête HTTP. Il n'ajoute le jeton qu'aux appels à `/api/`. Une requête vers un autre site ne doit jamais l'emporter. Un test le vérifie.

## 6. Le relais : une seule origine

```json
"/api": { "target": "http://localhost:5080", "pathRewrite": { "^/api": "" } }
```

Le navigateur ne parle qu'à `localhost:4200`. Le serveur de dev transmet `/api/llm` à l'API sous le nom `/llm`. L'API n'a **pas besoin de CORS**.

Pourquoi le préfixe `/api` ? Parce que `/missions` est aussi une **page** de l'admin. Sans préfixe, recharger la page `/missions` partirait vers l'API.

## 7. La garde

```ts
export const adminGuard: CanActivateFn = () =>
  inject(AuthService).isAdmin() || inject(Router).parseUrl('/interdit');
```

Les rôles sont lus dans le jeton (`realm_access.roles`, comme côté API à la leçon 30). Cette garde est un **confort** : l'API refuse de toute façon ce qu'un compte n'a pas le droit de faire.

## 8. Vérifie toi-même

```bash
docker compose up -d --force-recreate keycloak   # réimporte le realm avec le nouveau client
dotnet run --project src/AgentYattus.Api
cd src/AgentYattus.Admin && npm start
```

Ouvre <http://localhost:4200> et connecte-toi avec `dev`, puis avec `observateur` : la seconde fois, l'accès est refusé.

## 9. Quiz

1. Pourquoi le client Keycloak de l'admin est-il public ?
2. Pourquoi les jetons ne vont-ils pas dans `sessionStorage`, alors que le secret PKCE y va ?
3. Que se passerait-il si on acceptait n'importe quelle adresse de retour après la connexion ?
4. Pourquoi les appels à l'API passent-ils par `/api` ?

## 10. Pour aller plus loin

- oidc-client-ts : <https://github.com/authts/oidc-client-ts>
- OAuth 2.0 pour les applications dans le navigateur (IETF) : <https://datatracker.ietf.org/doc/html/draft-ietf-oauth-browser-based-apps>
- Le relais du serveur de dev d'Angular : <https://angular.dev/tools/cli/serve#proxying-to-a-backend-server>

<details>
<summary>Réponses du quiz</summary>

1. Tout le code d'une page web est lisible : un secret de client n'y resterait pas secret. PKCE remplace le secret.
2. Le secret PKCE ne sert qu'une fois, pendant quelques secondes ; un jeton donne accès à l'API pendant des minutes. Moins un jeton est exposé, mieux c'est.
3. Un lien piégé pourrait renvoyer la victime, juste après sa connexion, vers un faux site : c'est une redirection ouverte.
4. Pour séparer les pages de l'admin (`/missions`) des routes de l'API (`/api/missions`) et relayer seulement ces dernières.

</details>
