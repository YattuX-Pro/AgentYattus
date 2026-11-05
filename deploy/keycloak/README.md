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
