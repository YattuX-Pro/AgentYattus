# Keycloak de développement

`agent-yattus-realm.json` est importé au premier démarrage de `docker compose up`.

**Réservé au développement local** : le conteneur tourne en mode `start-dev` (HTTP, base intégrée) et le realm contient
deux comptes de test aux mots de passe connus. Ne jamais déployer ce fichier tel quel.

| Compte | Mot de passe | Rôles |
|---|---|---|
| `dev` | `agent-yattus-dev` | `llm-admin` (gère les clés et le modèle) |
| `observateur` | `agent-yattus-observateur` | aucun rôle particulier |

Console d'administration : <http://localhost:8380/admin> (compte `admin`, mot de passe défini par `KEYCLOAK_ADMIN_PASSWORD`,
`admin` par défaut en développement).

## Clients

| Client | Application | Redirection |
|---|---|---|
| `agent-yattus-desktop` | Bureau (Avalonia) | `http://127.0.0.1/*` (boucle locale, port libre) |
| `agent-yattus-admin` | Espace admin web (Angular) | `http://localhost:4200/*` |

Les deux sont **publics** (aucun secret ne peut rester caché dans une app de bureau ou une page web) et imposent **PKCE**.

⚠️ Keycloak n'importe le realm qu'au **premier** démarrage du conteneur. Pour qu'il prenne un client ajouté ensuite,
recréer le conteneur : `docker compose up -d --force-recreate keycloak` (les comptes créés à la main sont perdus :
en `start-dev`, la base de Keycloak vit dans le conteneur).

## Thème de connexion

`themes/agent-yattus/` suit la maquette https://claude.ai/artifact/HWDHft7SQGMeAfXwY7ibuy : à gauche, un visuel sombre du réseau d'agents ; à droite, le formulaire, qui suit le mode clair ou sombre du système.

- `template.ftl` : l'en-tête de `keycloak.v2` 26.8.1 (scripts, mode sombre), avec un corps en deux colonnes. À reprendre lors d'une mise à jour de Keycloak.
- `login.ftl` : le formulaire de connexion redessiné. Les champs et leurs noms ne changent pas.
- Les autres pages (mot de passe à changer, OTP, erreurs) gardent les composants de `keycloak.v2`, recolorés par `agent-yattus.css`.

Le realm active le thème (`loginTheme`), affiche la page en français seulement et propose « Se souvenir de moi ». « Mot de passe oublié ? » n'apparaîtra que lorsque la réinitialisation sera activée, ce qui demande un serveur d'e-mail.

Le cache des thèmes est désactivé dans `docker-compose.yml` : une retouche se voit au simple rechargement de la page. Un changement du realm, lui, demande de recréer le conteneur.

## Google et GitHub

La page de connexion web et l'écran du bureau proposent « Continuer avec Google » et « Continuer avec GitHub ». Keycloak sert d'intermédiaire : l'application ne parle qu'à Keycloak et reçoit le même jeton qu'avec un compte local. Un utilisateur arrivé ainsi n'a aucun rôle d'administration.

1. Crée une application OAuth chez chaque fournisseur :
   - **Google** : console Google Cloud › API et services › Identifiants › ID client OAuth, type « Application Web ». URI de redirection : `http://localhost:8380/realms/agent-yattus/broker/google/endpoint`.
   - **GitHub** : Settings › Developer settings › OAuth Apps › New OAuth App. Authorization callback URL : `http://localhost:8380/realms/agent-yattus/broker/github/endpoint`.
2. Dans ton terminal, mets les identifiants en variables d'environnement, sans les écrire dans le dépôt : `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GITHUB_CLIENT_ID`, `GITHUB_CLIENT_SECRET`.
3. Lance `scripts/keycloak-social.sh`.

Un fournisseur sans identifiants est ignoré, et il n'apparaît pas sur la page web. Le script est à relancer après chaque recréation du conteneur, puisque le realm est alors réimporté sans eux.

Sur le bureau, les deux boutons sont toujours affichés. S'il ne connaît pas le fournisseur, Keycloak ouvre simplement son formulaire habituel.

