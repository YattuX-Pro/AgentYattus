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
