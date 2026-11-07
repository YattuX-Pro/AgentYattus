# ADR 0014 — Le mode local, et qui choisit le dossier d'un projet

- **Statut** : accepté
- **Date** : 2026-11-07
- **Précise** : ADR 0004 (backend hybride), ADR 0009 (dossier choisi par un administrateur)

## Contexte

Aujourd'hui, essayer Agent Yattus demande Docker, PostgreSQL, Keycloak et l'API. Ensuite, seul un administrateur de l'instance peut choisir le dossier d'un projet. Un développeur ne peut donc pas pointer vers son dépôt, et une personne qui n'est pas développeuse ne peut même pas installer le logiciel.

L'ADR 0004 a décidé le principe : le même binaire d'API, en local ou sur un serveur. Il restait à trancher les détails.

## Décision

### 1. Le mode local

- **Le logiciel lance l'API** (sidecar). Elle écoute sur `127.0.0.1`, sur un port libre choisi au lancement, et s'arrête avec le logiciel.
- **La base est SQLite**, un fichier dans le dossier de données de l'utilisateur :
  - `~/Library/Application Support/AgentYattus` sous macOS ;
  - `%LOCALAPPDATA%\AgentYattus` sous Windows.
- **L'accès passe par un jeton aléatoire** de 256 bits, tiré par le logiciel à chaque lancement et transmis à l'API par une variable d'environnement (`AGENTYATTUS_LOCAL_TOKEN`). Chaque requête doit le présenter, comparé en temps constant.
  - Un autre programme de la machine qui joindrait le port est refusé.
  - Le jeton ne passe ni sur la ligne de commande (visible avec `ps`), ni sur le disque.
- **Pas de Keycloak.** La personne de la machine est l'unique utilisatrice, et l'administratrice de son instance (`llm-admin`). Son identifiant stable est `local`.
- **Les clés des fournisseurs** sont chiffrées en base comme en mode serveur (Data Protection). Le trousseau vit dans le dossier de données, `keys/`. Le trousseau du système (Keychain, DPAPI) est reporté : il demanderait du code par plateforme.
- **Le portail d'administration web n'est pas lancé en mode local.** Ce dont une personne seule a besoin (connecter un modèle, choisir un dossier) se fait dans le logiciel.

### 2. Qui choisit le dossier d'un projet

- **En mode local** : la personne, n'importe quel dossier de sa machine. C'est sa machine.
- **En mode serveur** : le propriétaire du projet, mais seulement **sous un des dossiers autorisés par l'administrateur** (`Projects:AllowedRoots`). L'administrateur garde la main sur ce que le serveur expose. Il peut toujours choisir n'importe quel dossier, comme avant.
- Dans les deux cas, le dossier doit exister. Les règles de `WorkspaceTools` s'appliquent ensuite sans changement : confinement, liens symboliques, fichiers exclus.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Garder Keycloak en local, dans un conteneur | Un seul chemin d'authentification | Docker obligatoire : c'est ce qu'on veut supprimer |
| Pas d'authentification en local (boucle locale seulement) | Le plus simple | N'importe quel programme ou page web de la machine piloterait les agents (requêtes vers localhost) |
| **Jeton par lancement** | Aucun service à installer ; refuse les autres programmes | Le jeton vit en mémoire dans deux processus |
| Dossier libre pour tous en mode serveur | Simple | Un utilisateur ferait lire `/etc` ou les dossiers des autres au serveur |

## Conséquences

- (+) On installe le logiciel et on travaille. Ni Docker ni compte ne sont nécessaires.
- (+) Un développeur pointe vers son dépôt en un clic.
- (−) SQLite crée ses tables sans migrations (`EnsureCreated`), comme dans les tests. Une évolution du schéma demandera, pour le mode local, sa propre série de migrations (ADR 0004, point 4). C'est noté `ponytail` dans `Database.cs`.
- (−) Un seul utilisateur en mode local. Pour partager, il faut le mode serveur.
