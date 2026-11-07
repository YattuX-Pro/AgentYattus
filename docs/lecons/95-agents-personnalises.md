# Leçon 95 — Des agents créés dans l'admin : un catalogue plutôt qu'une liste figée

> **Commit** : `feat(agents): ajouter, modifier et retirer des agents`
> **Phase** : le bureau des agents
> **Fichiers** : `Agents/` (`AgentRoles.cs`, `AgentDefinitions.cs`, `AgentsModule.cs`, `AgentFrameworkRunner.cs`, `Migrations/`), `Missions/` (`Mission.cs`, `MissionsModule.cs`, `MissionWorker.cs`, `Migrations/`), `Team/`, contrats, tests

## 1. Ce qu'on construit

- `POST /agents` : créer un agent (nom, présentation, consigne, outils pris dans la liste).
- `PUT /agents/{clé}/profile` : changer son nom, sa présentation, ses outils. `DELETE /agents/{clé}` : le retirer.
- `CreateMissionRequest.Agent` : l'agent qui fait une mission « Question ». La relance le garde.

## 2. D'une liste figée à un catalogue

`AgentRoles.All` était une liste écrite dans le code. Elle reste (l'Analyste, le Rédacteur), et `AgentCatalog` y ajoute les agents de la base. Les autres modules ne voient que l'interface :

```csharp
public interface IAgentCatalog { Task<IReadOnlyList<string>> RoleKeysAsync(CancellationToken ct); }
```

Missions vérifie l'agent choisi, Équipe vérifie le rôle d'un siège ; ni l'un ni l'autre ne connaît la table `CustomRoles`.

## 3. L'identifiant ne change jamais

« Testeur de régression » devient `testeur-de-regression`, une fois pour toutes. Le journal (`usage:testeur-de-regression:…`), les statistiques, les sièges et les versions de consigne s'y rattachent : renommer l'agent ne casse rien. Un second agent du même nom reçoit `-2`.

## 4. Retirer, pas supprimer

Un agent retiré n'est plus proposé, mais ses missions passées gardent son nom, et une mission **en pause** peut encore reprendre : le runner le retrouve même retiré. Supprimer la ligne aurait laissé une décision humaine sans agent pour l'exécuter.

## 5. La sécurité ne dépend pas du formulaire

Un test le vérifie de bout en bout : un agent créé avec `write_file` reçoit **seulement** cet outil, sa consigne se termine par « Chaque écriture attend la décision d'un humain. », et la mission s'arrête bien en attente d'accord.

## 6. Quiz

1. Pourquoi l'identifiant d'un agent ne suit-il pas les changements de nom ?
2. Pourquoi un agent retiré peut-il encore terminer une mission en pause ?

<details>
<summary>Réponses du quiz</summary>

1. Le journal, les statistiques et les sièges s'y rattachent : il doit rester stable.
2. La mission attend une décision humaine ; sans agent pour reprendre, cette décision ne pourrait plus aboutir.

</details>
