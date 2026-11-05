# ADR 0004 — Backend hybride : le même binaire, en local ou sur un serveur

- **Statut** : proposé
- **Date** : 2026-11-05

## Contexte

Deux usages d'Agent Yattus coexistent :

- **En local** : une personne installe le desktop et lance des missions sur ses propres dossiers, sans serveur à louer ni Docker à installer.
- **Sur un serveur** : une équipe partage les mêmes missions, et le téléphone suit et approuve à distance.

Aujourd'hui, l'API tourne sur la machine du développeur avec PostgreSQL dans Docker, et n'écoute que sur `localhost`. Les clients ne connaissent qu'une URL de base (`http://localhost:5080/`, ou la variable `AGENTYATTUS_API_URL` côté desktop).

## Décision

1. **Un seul binaire d'API, deux modes choisis par la configuration.** Il n'y a pas de deuxième backend : seuls la base de données, l'adresse d'écoute et la sécurité changent.

| | Mode local | Mode serveur |
|---|---|---|
| Base | SQLite, un fichier dans le dossier de données de l'utilisateur | PostgreSQL |
| Lancement | Par le desktop, comme processus enfant (*sidecar*), arrêté avec lui | Par l'hébergeur (conteneur Docker) |
| Écoute | `127.0.0.1` uniquement, sur un port choisi au lancement | HTTPS, derrière un reverse proxy |
| Accès | Un jeton aléatoire généré à chaque lancement, transmis au desktop et exigé à chaque requête | Authentification des utilisateurs (ADR à venir) |
| Clé du fournisseur de modèle | Celle de l'utilisateur, dans le coffre sécurisé du système | Dans les secrets du serveur, jamais sur les clients |

2. **Les clients ne connaissent qu'une URL** (et, plus tard, un moyen de s'authentifier). Ils ne savent pas dans quel mode tourne l'API.
3. **Le code des modules reste compatible avec les deux bases** : pas de fonction propre à PostgreSQL (comme `jsonb`) sans équivalent prévu pour SQLite.
4. **Une série de migrations EF Core par fournisseur de base**, comme le recommande la documentation d'EF Core.
5. **Pas d'exposition au réseau sans authentification.** Tant que l'authentification n'existe pas, l'API reste sur la boucle locale (`AllowedHosts: localhost;127.0.0.1;[::1]`).

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Serveur uniquement | Une seule configuration | Il faut un serveur même pour un usage solo ; impossible hors ligne |
| Local uniquement | Simple, aucune donnée ne quitte la machine | Pas de partage ni d'accès mobile à distance |
| Deux backends différents | Chacun optimisé pour son usage | Double code, double bugs |
| Backend intégré au processus du desktop | Pas de processus séparé à gérer | Le mobile ne peut pas s'y connecter ; une erreur de l'interface arrête les missions ; deux façons d'appeler le même code |
| **Même binaire, deux modes** | Un seul code, testé dans les deux configurations | Le desktop doit gérer un processus enfant ; deux séries de migrations |

## Conséquences

- (+) Un seul code backend à maintenir.
- (+) Il y a déjà un indice que c'est faisable : les tests de l'API tournent sur SQLite avec le même modèle de données que PostgreSQL (`tests/AgentYattus.Api.Tests/ApiFactory.cs`).
- (−) SQLite a des limites : pas de schémas, un seul écrivain à la fois, certaines modifications de tables non prises en charge par les migrations. C'est acceptable pour un usage local à un seul utilisateur.
- (−) Le desktop doit lancer, surveiller et arrêter l'API (commit `feat(desktop): backend local en sidecar`, après le MVP).
- (−) En mode local, n'importe quel programme de la machine pourrait joindre l'API : d'où le jeton par lancement.
- (−) Le mobile ne joint que le mode serveur. Joindre un desktop sur le réseau local demanderait l'authentification de toute façon.
- Rien n'est implémenté par cet ADR. Le choix du fournisseur de base, le sidecar et le jeton arriveront avec les commits `feat(auth)` et `feat(desktop): backend local en sidecar`. Le stockage exact de la clé en mode local sera précisé à ce moment-là.

## Sources

- Migrations avec plusieurs fournisseurs : <https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers>
- Limites de SQLite avec EF Core : <https://learn.microsoft.com/ef/core/providers/sqlite/limitations>
- Adresses d'écoute de Kestrel : <https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints>
- Secrets en développement : <https://learn.microsoft.com/aspnet/core/security/app-secrets>
