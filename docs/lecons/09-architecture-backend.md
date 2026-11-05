# Leçon 09 — L'architecture du backend

> **Commit** : `docs: ADR 0003, architecture backend`
> **Phase** : 1, Agent Framework
> **Fichiers** : `docs/adr/0003-architecture-backend.md` (le code décrit est dans `src/AgentYattus.Api`)

## 1. Ce qu'on construit

Aucun code dans ce commit : on **écrit pourquoi** le backend est construit comme il l'est. Ce document s'appelle un **ADR**, pour *Architecture Decision Record*, ou « fiche de décision d'architecture ».

> **Une image** : le code dit *comment* la maison est construite. L'ADR est le carnet de l'architecte : il dit *pourquoi* le mur porteur est à cet endroit, et quelles autres options ont été écartées. Dans deux ans, personne ne se souviendra de la raison sans ce carnet.

Un ADR a toujours la même forme : **Contexte** (la situation), **Décision** (ce qu'on choisit), **Alternatives** (ce qu'on écarte et pourquoi), **Conséquences** (les avantages, et surtout les inconvénients qu'on accepte).

Son **statut** est « proposé » : il devient « accepté » quand tu l'as lu et validé.

## 2. Le problème

Le backend doit :

1. recevoir une mission depuis le desktop ou le téléphone ;
2. la faire exécuter par un agent, ce qui peut prendre **plusieurs minutes** ;
3. garder une trace de chaque étape ;
4. prévenir l'écran **en direct** quand quelque chose change.

Le point 2 pose une vraie difficulté : une requête HTTP ne doit pas rester ouverte plusieurs minutes. Le navigateur, le réseau ou le téléphone finiraient par couper.

## 3. Les grandes notions, une par une

### 3.1 Monolithe modulaire

- Un **monolithe**, c'est **une seule application**, lancée comme un seul programme.
- Des **microservices**, ce sont **plusieurs petites applications** qui se parlent par le réseau.
- Un **monolithe modulaire**, c'est un seul programme, mais **rangé en tiroirs étanches** : les modules.

Agent Yattus a trois modules :

| Module | Rôle | Ce qu'il montre aux autres |
|---|---|---|
| `Missions` | Créer, lire et exécuter les missions | — |
| `Agents` | Faire travailler l'agent | `IAgentRunner` |
| `Audit` | Noter chaque événement | `AuditLog` |

**La règle** : un module ne touche que ce que les autres **montrent** (leurs types `public`). Tout le reste est `internal`. Comme les trois modules vivent dans le même projet, le compilateur ne peut pas faire respecter cette règle. C'est donc un **test** qui la vérifie : `tests/AgentYattus.Api.Tests/ArchitectureTests.cs`.

**Pourquoi pas des microservices ?** Pour une personne seule et un MVP, c'est beaucoup trop : réseau entre services, déploiements multiples, pannes partielles. Le monolithe modulaire garde la porte ouverte : un module bien isolé peut devenir un service plus tard.

### 3.2 Un schéma de base de données par module

Chaque module a **ses propres tables**, rangées dans son **schéma** PostgreSQL (`missions`, `audit`). Un schéma est un dossier de tables à l'intérieur de la base. Aucun module ne lit les tables d'un autre : il passe par ce que l'autre montre.

### 3.3 Répondre tout de suite : 202 Accepted

Quand le desktop envoie `POST /missions`, l'API :

1. enregistre la mission avec le statut `Pending` ;
2. la pose dans une **file d'attente** ;
3. répond **immédiatement** `202 Accepted`, ce qui veut dire « bien reçu, je m'en occupe ».

> Pourquoi 202 et pas 201 ? `201 Created` voudrait dire « c'est fait ». Or la mission n'est pas encore exécutée, seulement acceptée.

### 3.4 Travailler en arrière-plan : la file et le service

```
 POST /missions ──► [ file d'attente (Channel) ] ──► MissionWorker (BackgroundService)
       │                                                    │
   répond 202                                     exécute la mission avec l'agent
```

- La **file** est un `Channel` : un tuyau en mémoire. L'API pose des identifiants d'un côté, le service les reprend de l'autre.
- Le **service d'arrière-plan** (`MissionWorker`) tourne en permanence dans l'application. Il prend une mission, la passe à `Running`, appelle l'agent, puis la passe à `Completed` ou à `Failed`.
- Si une mission plante, elle passe à `Failed`, et **le service continue** avec la suivante.

### 3.5 Prévenir l'écran en direct : SignalR

SignalR garde une **connexion ouverte** entre l'API et chaque client. À chaque changement de statut, l'API envoie un message `MissionEvent`, et la carte de la mission change de couleur à l'écran, sans que le client ait à redemander.

L'alternative, le *polling*, consisterait pour le client à demander toutes les deux secondes « alors, c'est fini ? ». C'est plus lent, et ça génère des requêtes inutiles.

## 4. Le trajet complet d'une mission

```
 Desktop                     API                                         Base
   │ POST /missions ───────► valide le but                               │
   │                         enregistre (Pending) ─────────────────────► missions.Missions
   │ ◄────── 202 Accepted    pose l'id dans la file                      │
   │                         MissionWorker : statut Running ───────────► + audit.Events
   │ ◄────── MissionEvent(Running)   (SignalR)                           │
   │                         IAgentRunner.RunAsync(…)                    │
   │                         statut Completed ─────────────────────────► + audit.Events
   │ ◄────── MissionEvent(Completed) → la carte devient « Terminée »     │
```

## 5. Les inconvénients acceptés

Un bon ADR dit honnêtement ce qui manque :

- **La file est en mémoire** : si l'API redémarre, une mission en attente est perdue, et une mission en cours reste bloquée à `Running`.
- **Une seule mission à la fois.**
- **Pas encore de migrations** : les tables sont créées au démarrage.
- **Chaque événement part à tous les clients**, pas seulement à celui qui est concerné.

Chacun est marqué dans le code par un commentaire `ponytail:`, et sera traité à une étape précise de la roadmap.

## 6. Vérifie toi-même

Le test qui fait respecter les frontières entre modules :

```bash
dotnet run --project tests/AgentYattus.Api.Tests -- -class "AgentYattus.Api.Tests.ArchitectureTests"
```

Puis ouvre `src/AgentYattus.Api/Modules/Missions/MissionWorker.cs` et repère :

- la boucle `await foreach` qui lit la file ;
- le `catch` qui empêche une mission en échec d'arrêter le service ;
- `SetStatusAsync`, qui fait trois choses à chaque changement : enregistrer, noter dans l'audit, prévenir les clients.

## 7. Quiz

1. Quelle différence entre un monolithe modulaire et des microservices ?
2. Pourquoi `POST /missions` répond-il 202 et pas 201 ?
3. Que se passe-t-il pour une mission en attente si l'API redémarre ?
4. Pourquoi faut-il un test pour faire respecter les frontières entre modules ?

## 8. Pour aller plus loin

- L'ADR complet : `docs/adr/0003-architecture-backend.md`
- Architecture Decision Records : <https://adr.github.io>
- Services d'arrière-plan : <https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services>
- Channels : <https://learn.microsoft.com/dotnet/core/extensions/channels>
- SignalR : <https://learn.microsoft.com/aspnet/core/signalr/introduction>

<details>
<summary>Réponses du quiz</summary>

1. Un monolithe modulaire est un seul programme rangé en modules étanches. Des microservices sont plusieurs programmes qui se parlent par le réseau.
2. Parce que la mission est seulement acceptée, pas encore exécutée. 201 voudrait dire qu'elle est terminée.
3. Elle est perdue, parce que la file vit en mémoire. Sa ligne reste en base avec le statut `Pending`, mais personne ne la reprendra.
4. Parce que les trois modules sont dans le même projet : `internal` ne les sépare pas entre eux, donc le compilateur ne peut pas empêcher un module d'utiliser les types cachés d'un autre.

</details>
