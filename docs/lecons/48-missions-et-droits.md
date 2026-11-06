# Leçon 48 — Les missions suivent les droits : projet, auteur, direct ciblé

> **Commit** : `feat(missions): chaque mission appartient à un projet et à son auteur`
> **Phase** : multi-utilisateur
> **Fichiers** : `Modules/Missions/` (`MissionsModule.cs`, `Mission.cs`, `MissionWorker.cs`, migration), `Modules/Agents/` (`AgentsModule.cs`, `AgentFrameworkRunner.cs`), `Authentication.cs`, `Contracts/Missions.cs`, tests (`MissionOwnershipTests.cs`, `AuthTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

- Une mission **appartient à un projet** et garde son **auteur**. Sans projet précisé, elle va dans le projet personnel de l'auteur : le bureau fonctionne sans changement.
- Chaque route des missions **vérifie les droits** : lister, lire, lire le journal, décider, créer.
- L'agent travaille dans le **dossier du projet**.
- Le **direct** (SignalR) n'atteint que les personnes qui peuvent lire la mission.
- Un jeton sans `sub` est **refusé**.

## 2. Vérifier à chaque porte

| Route | Sans accès | Lecteur | Éditeur, propriétaire |
|---|---|---|---|
| `GET /missions` | absente de la liste | ✓ | ✓ |
| `GET /missions/{id}` et `/events` | 404 | ✓ | ✓ |
| `POST /missions/{id}/decision` | 404 | 403 | ✓ |
| `POST /missions` dans ce projet | 404 | 403 | ✓ |

Un oubli sur **une seule** route suffit à laisser fuir les autres. Le test `A_mission_belongs_to_its_project_and_strangers_see_nothing` passe donc par **toutes** les routes avec un second utilisateur.

> **IDOR** (*Insecure Direct Object Reference*) : deviner l'identifiant d'un objet d'un autre et le lire. C'est l'une des failles les plus courantes des API. La parade : vérifier l'accès **à l'objet**, pas seulement la connexion.

## 3. Filtrer dans la base

```csharp
if (await projects.ReadableAsync(cancellationToken) is { } readable)
    query = query.Where(m => m.ProjectId != null && readable.Contains(m.ProjectId.Value));
```

`ReadableAsync` renvoie `null` pour un administrateur de l'instance (« tout »), sinon la liste des projets lisibles. Le module Missions ne connaît pas la règle d'accès : il la demande à Projects (ADR 0003, une dépendance dans un seul sens).

## 4. Le direct ciblé

```csharp
var readers = await projects.ReadersAsync(mission.ProjectId ?? Guid.Empty, cancellationToken);
await hub.Clients.Users(readers).SendAsync(MissionsHubProtocol.MissionEvent, ...);
```

`Clients.All` envoyait chaque événement à tout le monde : le but de la mission ne partait pas, mais son existence et son rythme, si. `Clients.Users(...)` vise des personnes. SignalR doit savoir qui est qui : `SubUserIdProvider` lui dit d'utiliser `sub` (leçon 46).

## 5. Le dossier du projet

```csharp
workspace ??= settings.Workspace;
var tools = new WorkspaceTools(workspace).AsAIFunctions();
```

L'exécuteur reçoit le dossier du projet, choisi par un administrateur de l'instance (leçon 47). La barrière de `WorkspaceTools` reste la même.

⚠️ **Trouvé par la revue de sécurité** : la première version se rabattait sur le dossier configuré du serveur (`Agents:Workspace`) pour **tout** projet sans dossier. N'importe qui pouvait donc créer un projet personnel et faire lire ce dossier commun aux agents : l'isolation entre projets était contournée. Désormais :

```csharp
if (project.Workspace is not null) return new ProjectWorkspace(true, project.Workspace);
return new ProjectWorkspace((await directory.InstanceAdminsAsync(ct)).Contains(project.OwnerId), null);
```

Le dossier configuré ne sert qu'aux projets d'un administrateur de l'instance (celui qui l'a configuré). Pour les autres, la mission échoue tout de suite, **avant** d'appeler l'agent, avec un message qui dit quoi faire. Un test vérifie que l'agent n'a jamais été lancé.

## 6. Les missions d'avant

La migration ajoute `ProjectId`, `CreatedBy` et `CreatedByName`, **nuls** pour les missions existantes. On ne devine pas à qui elles appartenaient : seuls les administrateurs de l'instance les voient. Mieux vaut cacher une mission ancienne que la montrer à tort.

## 7. Pas de `sub`, pas d'accès

```csharp
if (context.Principal?.FindFirst("sub")?.Value is not { Length: > 0 and <= 64 })
    context.Fail("Jeton sans identifiant d'utilisateur (sub).");
```

Les droits sont rattachés à `sub`. Un jeton qui n'en a pas est refusé dès l'authentification (401), plutôt que de provoquer une erreur 500 plus loin. Le banc d'évaluation, qui a son propre faux utilisateur, a dû recevoir un `sub` : c'est ce qui a révélé le cas.

## 8. Quiz

1. Qu'est-ce qu'une faille IDOR, et comment ce commit s'en protège-t-il ?
2. Pourquoi tester toutes les routes avec un second utilisateur ?
3. Que révélait `Clients.All`, même sans le contenu de la mission ?
4. Pourquoi les missions anciennes ne sont-elles visibles que des administrateurs ?

<details>
<summary>Réponses du quiz</summary>

1. Lire l'objet d'un autre en devinant son identifiant ; chaque route vérifie l'accès au projet de la mission.
2. Parce qu'une seule route oubliée suffit à faire fuir les données.
3. Qu'une mission existait, quand elle démarrait et finissait.
4. On ne sait pas à qui elles appartenaient : les cacher est plus sûr que de les montrer à tort.

</details>
