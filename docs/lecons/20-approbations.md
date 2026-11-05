# Leçon 20 — L'humain dans la boucle : pause et reprise sur action sensible

> **Commit** : `feat(approvals): pause et reprise sur action sensible`
> **Phase** : 2, backend
> **Fichiers** : `Modules/Agents/` (`WorkspaceTools.cs`, `AgentFrameworkRunner.cs`, `AgentsModule.cs`), `Modules/Missions/` (`Mission.cs`, `MissionWorker.cs`, `MissionsModule.cs`, `Migrations/…_AddApprovals.cs`), `src/AgentYattus.Contracts/Missions.cs`, `tests/AgentYattus.Api.Tests/ApprovalTests.cs`

C'est la promesse centrale d'Agent Yattus : **rien de sensible ne se passe sans ton accord.**

## 1. Ce qu'on construit

L'agent reçoit un outil qui **modifie** ton disque : `write_file`. Mais avant chaque écriture :

1. l'agent **s'arrête** ;
2. la mission passe au statut **`AwaitingApproval`** et montre **exactement** ce qu'il veut écrire ;
3. tu **approuves** ou tu **refuses** ;
4. l'agent **reprend** : il écrit (si tu as approuvé) ou apprend que c'est refusé, puis termine sa mission.

```
Pending → Running → AwaitingApproval ──(décision)──► Running → Completed
                         ⏸ l'exécuteur est libre pendant ce temps
```

> **Une image** : un stagiaire prépare un courrier important. Il ne l'envoie pas : il le pose sur ton bureau avec un post-it « à signer ». Pendant que tu le lis, il ne reste pas planté devant toi : il traite les autres dossiers. Quand tu signes (ou pas), il reprend ce courrier là où il l'avait laissé.

## 2. Les trois difficultés

1. **Arrêter l'agent au bon moment** : avant l'action, pas après.
2. **Libérer l'exécuteur** : une décision humaine peut prendre des heures. Une seule mission en pause ne doit pas bloquer toutes les autres (on n'a qu'un exécuteur, leçon 13).
3. **Reprendre exactement où on en était** : même si l'API a redémarré entre-temps.

## 3. Arrêter l'agent : `ApprovalRequiredAIFunction`

```csharp
new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
    WriteFile, "write_file", "Crée ou remplace un fichier texte du workspace. Un humain doit approuver chaque écriture."))
```

On **enveloppe** l'outil dans `ApprovalRequiredAIFunction`. Quand le modèle demande `write_file`, la couche de boucle (`FunctionInvokingChatClient`, leçon 08) **ne l'exécute pas**. Elle s'arrête et renvoie à la place une **demande d'approbation** (`ToolApprovalRequestContent`), qui contient l'appel exact : nom de l'outil et arguments.

La règle est posée **dans la définition de l'outil**, au plus près du code qui touche le disque. Aucun prompt ne peut la contourner (leçon 03).

`write_file` passe d'ailleurs par la même barrière que les autres outils : pas de sortie du workspace, pas de liens symboliques, taille limitée. **Approuver ne donne pas tous les droits.**

## 4. Libérer l'exécuteur : tout mettre en base

Quand l'agent demande une approbation, le runner renvoie un **résultat de pause** au lieu d'une réponse :

```csharp
public sealed record AgentOutcome(string? Answer, AgentApproval? Approval = null, string? Conversation = null);
```

Le module Missions enregistre alors trois choses dans la mission :

| Colonne | Contenu |
|---|---|
| `Conversation` | Toute la conversation de l'agent jusqu'ici, en JSON : de quoi reprendre |
| `PendingApproval` | Les actions qui attendent une décision (outil + arguments) |
| `Decision` | Vide pour l'instant ; recevra `true` ou `false` |

…puis il passe la mission à `AwaitingApproval`, et **l'exécuteur prend la mission suivante**.

Ces colonnes sont ajoutées par une **migration** (`AddApprovals`), comme on l'a appris à la leçon 19.

### Pourquoi la conversation entière ?

Rappel de la leçon 04 : **le modèle n'a pas de mémoire**. Pour reprendre, il faut lui renvoyer tout l'historique : le but, les fichiers qu'il a lus, sa demande d'écriture. Les types de `Microsoft.Extensions.AI` (messages, appels d'outils, demandes d'approbation) **savent se transformer en JSON** et revenir. On les stocke donc tels quels.

Pour le module Missions, cette conversation est **opaque** : il la garde sans la lire. Seul le module Agents sait l'interpréter. C'est la frontière entre modules de l'ADR 0003.

## 5. Décider : `POST /missions/{id}/decision`

```http
POST /missions/0193…/decision
{ "approved": true }
```

```csharp
var updated = await db.Missions
    .Where(m => m.Id == id && m.Status == MissionStatus.AwaitingApproval && m.Decision == null)
    .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Decision, request.Approved), cancellationToken);
```

Une **seule requête SQL** vérifie **et** écrit en même temps : « mets la décision **si** la mission attend **et** n'a pas encore de décision ».

> **Pourquoi pas lire, vérifier, puis écrire ?** Imagine que tu cliques « Approuver » sur le desktop et « Refuser » sur le téléphone au même instant. Les deux lisent « pas encore de décision », et les deux écrivent. Avec une seule requête, la base garantit qu'**une seule** passe. L'autre reçoit `409 Conflict`.

| Situation | Réponse |
|---|---|
| La mission attend, première décision | `202 Accepted`, la mission est remise dans la file |
| Décision déjà prise, ou mission qui n'attend rien | `409 Conflict` |
| Mission inconnue | `404 Not Found` |

La décision est inscrite dans l'audit : `decision:approuvee` ou `decision:refusee`.

## 6. Reprendre

Quand l'exécuteur retrouve la mission dans la file, il voit `AwaitingApproval` avec une décision, et reprend :

```csharp
var answered = contents.OfType<ToolApprovalResponseContent>().Select(r => r.RequestId).ToHashSet();
var pending = contents.OfType<ToolApprovalRequestContent>()
    .Where(request => !answered.Contains(request.RequestId))
    .Select(request => (AIContent)request.CreateResponse(resume.Approved))
    .ToList();
history.Add(new ChatMessage(ChatRole.User, pending));
```

On recharge la conversation, et on y ajoute **la réponse humaine** à chaque demande encore sans réponse. Puis on relance l'agent avec cet historique :

- **approuvé** : la couche de boucle exécute enfin `write_file`, renvoie le résultat au modèle, qui conclut ;
- **refusé** : la couche de boucle renvoie au modèle un résultat « refusé » pour son appel. Le modèle **sait** que l'écriture n'a pas eu lieu, et peut l'expliquer.

On ne répond qu'aux demandes **sans réponse**. Si l'agent demande une deuxième approbation plus tard dans la même mission, l'ancienne demande, déjà tranchée, n'est pas re-décidée.

## 7. Ce que montre l'audit

```
Running
approbation-demandee:write_file
AwaitingApproval
decision:approuvee
Running
outil:write_file
Completed
```

Toute l'histoire de la mission, lisible : qui a demandé quoi, qui a décidé, ce qui a été exécuté.

## 8. Les tests

| Test | Vérifie |
|---|---|
| `Approved_write_is_executed_and_the_mission_completes` | Rien n'est écrit avant la décision ; après approbation, le fichier contient `# Rapport` ; l'audit complet ci-dessus |
| `Refused_write_is_not_executed_and_the_model_is_told` | Aucun fichier ; le modèle a reçu un résultat pour son appel refusé |
| `A_paused_mission_does_not_block_the_next_one` | Pendant que A attend, B est exécutée jusqu'au bout |
| `A_decision_is_refused_when_nothing_is_awaiting_and_accepted_only_once` | 404, puis 202, puis 409 pour la 2ᵉ décision et pour une décision tardive |

Vérifié aussi sur un **vrai PostgreSQL** jetable : la migration s'applique, et la décision atomique répond 202 puis 409.

## 9. Les limites acceptées

- **Une décision pour toutes les demandes d'un même tour.** Si l'agent demande deux écritures d'un coup, approuver approuve les deux. L'API les montre **toutes** dans `PendingApproval` : le client devra toutes les afficher avant le bouton.
- **Une mission dont l'API redémarre pendant la pause** est bien conservée (tout est en base). Mais si l'API redémarre **après** la décision et **avant** la reprise, la mission reste en attente avec sa décision : la reprise au démarrage est prévue après le MVP (ADR 0003).
- **Le desktop** (pas encore commité) ne connaît pas encore le statut `AwaitingApproval` : la carte d'approbation arrive au commit 25.

## 10. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests -- -class "AgentYattus.Api.Tests.ApprovalTests"
```

Avec une vraie clé (leçon 17), demande à l'agent d'écrire un fichier, puis :

```bash
curl http://localhost:5080/missions/<id>
```

```bash
curl -X POST http://localhost:5080/missions/<id>/decision -H "Content-Type: application/json" -d '{"approved":true}'
```

## 11. Quiz

1. Où est posée la règle « write_file demande une approbation », et pourquoi pas dans le prompt ?
2. Pourquoi enregistre-t-on toute la conversation de l'agent, et pas seulement l'action en attente ?
3. Pourquoi la décision est-elle écrite par une seule requête SQL ?
4. Que reçoit le modèle quand on refuse ?
5. Pourquoi une mission en attente ne bloque-t-elle pas les autres ?

## 12. Pour aller plus loin

- Approbation des outils dans Agent Framework : <https://learn.microsoft.com/agent-framework/agents/tools/tool-approval>
- `ExecuteUpdate` avec EF Core : <https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete>
- Building effective agents, section sur l'humain dans la boucle : <https://www.anthropic.com/engineering/building-effective-agents>

<details>
<summary>Réponses du quiz</summary>

1. Dans la définition de l'outil, avec `ApprovalRequiredAIFunction`. Un prompt ne fait que guider le modèle ; le code, lui, ne peut pas être contourné.
2. Parce que le modèle n'a pas de mémoire : pour reprendre, il faut lui renvoyer tout l'historique.
3. Pour que deux décisions simultanées ne puissent pas passer toutes les deux : la vérification et l'écriture sont indivisibles.
4. Un résultat « refusé » pour son appel d'outil : il sait que l'action n'a pas eu lieu.
5. Parce que son état est entièrement en base : l'exécuteur la quitte, et la décision la remet dans la file.

</details>
