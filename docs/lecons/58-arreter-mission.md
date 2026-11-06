# Leçon 58 — Arrêter une mission : annulation coopérative et courses

> **Commit** : `feat(missions): arrêter une mission`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Missions/` (`MissionsModule.cs`, `MissionWorker.cs`), `Contracts/Missions.cs`, `Client.Core/MissionItem.cs`, tests (`MissionControlTests.cs`, `ApiFactory.cs`)

## 1. Ce qu'on construit

`POST /missions/{id}/cancel`, pour un éditeur du projet, et un nouveau statut **Cancelled** (« Arrêtée ») :

| État | Ce qui se passe |
|---|---|
| En file | Passe tout de suite à « Arrêtée » ; elle ne partira jamais |
| En cours | Son exécution est annulée ; le runner la termine en « Arrêtée » |
| En pause (approbation) | Passe à « Arrêtée » ; la conversation en attente est effacée |
| Déjà finie | 409 |

## 2. L'annulation est coopérative

En .NET, on n'interrompt pas une tâche de force. On lui **demande** de s'arrêter, par un `CancellationToken`, et elle vérifie ce jeton à chaque étape : chaque appel HTTP au modèle, chaque lecture de fichier. C'est l'**annulation coopérative**.

```csharp
public bool Cancel(Guid missionId)
{
    if (!_running.TryGetValue(missionId, out var entry)) return false;
    _running[missionId] = (entry.Source, true);   // « arrêtée par un humain »
    entry.Source.Cancel();
    return true;
}
```

`MissionCancellations` garde, pour chaque mission qui tourne, la source de son jeton. C'est le même jeton que celui de la durée maximum (leçon 53). Le drapeau `ByUser` permet au runner de distinguer « arrêtée par un humain » (statut Arrêtée) de « durée dépassée » (statut Échouée).

## 3. Deux filtres d'exception, dans l'ordre

```csharp
catch (Exception) when (!cancellationToken.IsCancellationRequested && cancellations.CancelledByUser(missionId))
{
    status = MissionStatus.Cancelled;
}
catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
{
    status = MissionStatus.Failed;
}
```

Le premier `catch` dont le filtre est vrai gagne. Un arrêt humain devient « Arrêtée », toute autre erreur « Échouée ». Si l'hôte s'arrête (`cancellationToken`), aucune des deux ne s'applique, et la mission ne passe pas à tort en échec.

## 4. Les courses (race conditions)

Que se passe-t-il si l'on clique sur Arrêter **pendant** que la mission démarre ?

- Le runner **enregistre le jeton avant toute chose**, avant même de lire la mission. Un arrêt demandé à ce moment trouve le jeton, l'annule, et la mission s'arrête dès son premier appel.
- Pour une mission en file ou en pause, une seule requête SQL **vérifie et écrit** : `UPDATE … WHERE Status = 'Pending' OR (Status = 'AwaitingApproval' AND Decision IS NULL)`. Deux clics simultanés ne produisent qu'un seul arrêt ; une décision et un arrêt simultanés ne peuvent pas passer tous les deux. C'est le même principe que les décisions (leçon 20).
- Le runner relit le statut au départ : une mission arrêtée en file ne part pas.

## 5. Tester une mission qui ne finit jamais

```csharp
if (goal.StartsWith(LongGoal, StringComparison.Ordinal))
    return WaitForCancellationAsync(cancellationToken);
```

Le faux exécuteur des tests sait maintenant simuler une mission longue : elle attend jusqu'à son annulation. Le test arrête une mission en file (qui ne doit jamais apparaître dans les buts exécutés), puis une mission en cours.

## 6. Quiz

1. Qu'est-ce que l'annulation coopérative ?
2. Pourquoi enregistrer le jeton avant de lire la mission ?
3. Pourquoi l'ordre des deux `catch` compte-t-il ?

<details>
<summary>Réponses du quiz</summary>

1. On demande à une tâche de s'arrêter par un jeton qu'elle vérifie elle-même ; on ne l'interrompt pas de force.
2. Pour qu'un arrêt demandé pendant le démarrage ne soit pas perdu.
3. Le premier filtre vrai gagne : l'arrêt humain doit être reconnu avant le cas général de l'échec.

</details>
