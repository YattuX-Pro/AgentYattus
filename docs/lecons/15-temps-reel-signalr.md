# Leçon 15 — Le temps réel avec SignalR

> **Commit** : `feat(api): temps réel avec SignalR`
> **Phase** : 2, backend
> **Fichiers** : `Modules/Missions/MissionWorker.cs`, `Modules/Missions/MissionsModule.cs`, `src/AgentYattus.Contracts/Missions.cs`, `tests/AgentYattus.Api.Tests/MissionsTests.cs`

## 1. Ce qu'on construit

Quand le statut d'une mission change, l'API **prévient immédiatement** tous les clients connectés. La carte de la mission change de couleur à l'écran, sans que le client ait rien demandé.

> **Une image** : la différence entre rappeler le restaurant toutes les cinq minutes pour savoir si ta table est prête, et recevoir un SMS au moment où elle l'est.

## 2. Le problème

En HTTP classique, **seul le client parle en premier** : il pose une question, le serveur répond. Pour savoir si une mission avance, le client devrait redemander sans arrêt (*polling*) :

- l'écran a du retard (jusqu'à l'intervalle choisi) ;
- la plupart des requêtes sont inutiles (« toujours en cours… »).

## 3. SignalR

**SignalR** garde une **connexion ouverte** entre le serveur et chaque client. Le serveur peut alors **pousser** un message à tout moment. Il choisit tout seul la meilleure technique disponible : WebSockets en priorité, sinon d'autres méthodes de repli.

### 3.1 Le hub

```csharp
// Serveur vers client uniquement : aucune méthode appelable.
internal sealed class MissionsHub : Hub;
```

Un **hub** est le point de rendez-vous des connexions. Celui-ci est vide : les clients **écoutent**, ils n'appellent rien. Moins de surface, c'est aussi moins de risques.

```csharp
services.AddSignalR();
app.MapHub<MissionsHub>(MissionsHubProtocol.Path);   // "/hubs/missions"
```

### 3.2 Envoyer un événement

```csharp
await hub.Clients.All.SendAsync(
    MissionsHubProtocol.MissionEvent,
    new MissionEventDto(missionId, newStatus, DateTimeOffset.UtcNow),
    cancellationToken);
```

- On ne peut pas utiliser le hub directement depuis le service d'arrière-plan : on passe par **`IHubContext<MissionsHub>`**, injecté dans `MissionRunner`.
- `Clients.All` : tous les clients connectés.
- `SetStatusAsync` fait maintenant **trois** choses : enregistrer le statut, l'inscrire dans l'audit, **prévenir les clients**.

### 3.3 Le contrat partagé

```csharp
public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
```

L'adresse du hub et le nom de l'événement sont des **constantes partagées** dans `Contracts`. Une faute de frappe dans l'API ou dans un client ne compilerait plus, au lieu de casser silencieusement.

## 4. Le test

```csharp
await using var hub = new HubConnectionBuilder()
    .WithUrl(new Uri(factory.Server.BaseAddress, MissionsHubProtocol.Path),
        options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
    .Build();
hub.On<MissionEventDto>(MissionsHubProtocol.MissionEvent, events.Enqueue);
await hub.StartAsync(Ct);
```

Le test se connecte au hub **comme un vrai client**, lance une mission, puis vérifie qu'il a reçu, dans l'ordre : `Running`, puis `Completed`.

`HttpMessageHandlerFactory` fait passer la connexion par l'API en mémoire du test, au lieu du réseau.

Le hub diffuse à **tous** : le test ne garde que les événements de **sa** mission, car d'autres tests ont pu lancer les leurs en même temps.

## 5. La limite acceptée

```csharp
// ponytail: diffusion à tous les clients ; des groupes par mission quand il y aura plusieurs utilisateurs.
```

Aujourd'hui, chaque client reçoit les événements de toutes les missions. Avec plusieurs utilisateurs, il faudra des **groupes** SignalR, pour qu'une personne ne reçoive que les siens.

## 6. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

## 7. Quiz

1. Pourquoi le *polling* est-il moins bon que SignalR ici ?
2. Pourquoi le hub n'a-t-il aucune méthode ?
3. Comment le service d'arrière-plan envoie-t-il un message sans être un hub ?
4. Pourquoi le nom de l'événement est-il une constante dans `Contracts` ?

## 8. Pour aller plus loin

- SignalR : <https://learn.microsoft.com/aspnet/core/signalr/introduction>
- Envoyer des messages hors d'un hub : <https://learn.microsoft.com/aspnet/core/signalr/hubcontext>

<details>
<summary>Réponses du quiz</summary>

1. Le polling ajoute du retard et fait surtout des requêtes inutiles. SignalR pousse l'information au moment exact où elle change.
2. Parce que les clients ne font qu'écouter : moins de méthodes, c'est moins de surface d'attaque.
3. Par `IHubContext<MissionsHub>`, injecté dans `MissionRunner`.
4. Pour que l'API et les clients utilisent exactement le même nom : une faute de frappe ne compilerait plus.

</details>
