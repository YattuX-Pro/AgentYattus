# Leçon 13 — Exécuter les missions en arrière-plan

> **Commit** : `feat(missions): exécution en arrière-plan`
> **Phase** : 2, backend
> **Fichiers** : `Modules/Missions/MissionWorker.cs`, `Modules/Missions/MissionsModule.cs`, `Modules/Agents/AgentsModule.cs`, `Program.cs`, `tests/AgentYattus.Api.Tests/ApiFactory.cs`, `MissionsTests.cs`

## 1. Ce qu'on construit

Jusqu'ici, une mission restait en `Pending` pour toujours. Maintenant, **un service travaille en arrière-plan** : il prend les missions une par une, les fait exécuter par un agent, et met à jour leur statut.

```
Pending ──► Running ──► Completed
                   └──► Failed
```

> **Une image** : un restaurant. Le serveur (l'API) prend la commande, la pose sur le passe-plat (la file) et revient tout de suite vers le client. En cuisine, le cuisinier (le service d'arrière-plan) prend les tickets dans l'ordre.

## 2. Le problème

Une mission d'agent peut durer **plusieurs minutes**. Si l'API faisait le travail pendant la requête HTTP, le client attendrait sans réponse, et la connexion finirait par être coupée. Il faut donc **répondre tout de suite** et **travailler à côté**.

## 3. Les trois pièces

### 3.1 La file : `MissionQueue`

```csharp
internal sealed class MissionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}
```

Un **`Channel`** est un tuyau entre deux parties du programme : l'une écrit, l'autre lit. `POST /missions` y dépose l'identifiant de la mission :

```csharp
await queue.Writer.WriteAsync(mission.Id, cancellationToken);
```

Elle est enregistrée en **singleton** : une seule file pour toute l'application.

### 3.2 Le service : `MissionWorker`

```csharp
internal sealed class MissionWorker(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var missionId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MissionRunner>().RunAsync(missionId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Mission {MissionId} interrompue", missionId);
            }
        }
    }
}
```

- Un **`BackgroundService`** démarre avec l'application et tourne tant qu'elle tourne.
- `await foreach … ReadAllAsync` : attendre la prochaine mission, la traiter, recommencer. Sans mission, il attend **sans consommer de processeur**.
- **Une portée par mission** (`CreateAsyncScope`) : chaque mission reçoit son propre `DbContext`. Un `DbContext` n'est pas fait pour vivre longtemps ni pour être partagé.
- Le `catch` : **une mission qui plante ne doit jamais arrêter le service**, sinon plus aucune mission ne serait traitée.

### 3.3 L'exécution d'une mission : `MissionRunner`

```csharp
await SetStatusAsync(MissionStatus.Running);
var status = MissionStatus.Completed;
try
{
    await agent.RunAsync(mission.Id, mission.Goal, cancellationToken);
}
catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
{
    status = MissionStatus.Failed;
}
await SetStatusAsync(status);
```

Le filtre `when (!cancellationToken.IsCancellationRequested)` est subtil. On ne filtre pas sur le **type** d'exception, mais sur le **jeton** :

- si l'**application s'arrête**, le jeton est annulé : on laisse l'exception remonter ;
- si l'agent subit un **délai HTTP dépassé**, .NET lève aussi une `TaskCanceledException`, mais le jeton, lui, n'est pas annulé : c'est un **échec** de la mission, marquée `Failed`.

## 4. Le module Agents

```csharp
public interface IAgentRunner
{
    Task RunAsync(Guid missionId, string goal, CancellationToken cancellationToken);
}
```

Le module Missions ne sait **pas** comment un agent travaille. Il connaît seulement cette interface. Pour l'instant, l'implémentation est **provisoire** :

```csharp
internal sealed class PlaceholderAgentRunner : IAgentRunner
{
    public Task RunAsync(...) => Task.CompletedTask;
}
```

Elle ne fait rien et réussit tout de suite. Le **vrai** agent, sur Agent Framework, la remplacera au commit 17, sans toucher au module Missions.

## 5. Les tests

Dans les tests, un faux agent (`FakeAgentRunner`) note les buts reçus. Si le but est `"simule un délai HTTP dépassé"`, il échoue comme le ferait un vrai délai dépassé.

| Test | Vérifie |
|---|---|
| `Mission_reaches_Completed` | une mission passe bien à `Completed`, et l'agent a reçu son but |
| `Runner_timeout_marks_mission_Failed_and_worker_keeps_running` | une mission en échec passe à `Failed`, **et la suivante est quand même traitée** |

## 6. Les limites acceptées

```csharp
// ponytail: file en mémoire ; au redémarrage, une mission en attente est perdue …
// ponytail: une seule mission à la fois …
```

Toutes deux sont décrites dans l'ADR 0003 et seront traitées plus tard.

## 7. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

## 8. Quiz

1. Pourquoi ne pas exécuter la mission pendant la requête `POST` ?
2. Pourquoi créer une portée (*scope*) par mission ?
3. Pourquoi le `catch` du service est-il indispensable ?
4. Comment distingue-t-on un arrêt de l'application d'un délai HTTP dépassé ?

## 9. Pour aller plus loin

- Services d'arrière-plan : <https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services>
- Channels : <https://learn.microsoft.com/dotnet/core/extensions/channels>
- Durée de vie d'un DbContext : <https://learn.microsoft.com/ef/core/dbcontext-configuration/>

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'elle peut durer plusieurs minutes : le client attendrait, et la connexion serait coupée.
2. Pour que chaque mission ait son propre `DbContext`, qui ne doit être ni partagé ni gardé longtemps.
3. Sans lui, une seule mission en échec arrêterait le service, et plus aucune mission ne serait traitée.
4. Par le jeton d'annulation : il est annulé si l'application s'arrête, mais pas lors d'un délai HTTP dépassé.

</details>
