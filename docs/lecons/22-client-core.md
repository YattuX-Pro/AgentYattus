# Leçon 22 — Le cœur partagé des clients : `Client.Core`

> **Commit** : `feat(client-core): client API, hub et ViewModel d'accueil`
> **Phase** : 3, client desktop
> **Fichiers** : `src/AgentYattus.Client.Core/` (`ApiClient.cs`, `MissionsHubClient.cs`, `HomeViewModel.cs`, `MissionItem.cs`), `tests/AgentYattus.Client.Core.Tests/`

## 1. Ce qu'on construit

Une bibliothèque qui contient **tout ce que les applications font, sauf l'affichage** :

- parler à l'API (`ApiClient`) ;
- écouter les événements en direct (`MissionsHubClient`) ;
- l'état et les actions de l'écran d'accueil (`HomeViewModel`, `MissionItem`).

Le desktop (Avalonia) et le mobile (MAUI) l'utiliseront **tous les deux**. C'est la décision de l'ADR 0001 : seules les vues sont écrites deux fois.

> **Une image** : le moteur et le tableau de bord d'une voiture. On peut changer la carrosserie (desktop ou téléphone), le moteur reste le même.

## 2. Le motif MVVM

**MVVM** (*Model – View – ViewModel*) sépare trois rôles :

| Rôle | Ici | Ce qu'il sait |
|---|---|---|
| **Model** | `MissionDto` (leçon 11) | Les données |
| **ViewModel** | `HomeViewModel`, `MissionItem` | L'état de l'écran et ses actions : le texte saisi, la liste des missions, « lancer » |
| **View** | `HomeView.axaml` (leçon 23) | Comment ça s'affiche : couleurs, tailles, placement |

La vue se **lie** (*binding*) au ViewModel : quand une propriété change, l'écran se met à jour tout seul. Le ViewModel ne connaît **aucune** technologie d'affichage. On peut donc le **tester sans écran**.

## 3. Le code expliqué

### 3.1 Des propriétés qui préviennent l'écran

```csharp
public sealed partial class HomeViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchMissionCommand))]
    public partial string Goal { get; set; } = "";
```

- `ObservableObject` et `[ObservableProperty]` viennent de **CommunityToolkit.Mvvm**. Un générateur de code écrit pour nous la mécanique qui **prévient** la vue à chaque changement (`INotifyPropertyChanged`).
- `[NotifyCanExecuteChangedFor]` : quand `Goal` change, le bouton « lancer » réévalue s'il doit être actif.

### 3.2 Des actions : les commandes

```csharp
private bool CanLaunchMission => !string.IsNullOrWhiteSpace(Goal);

[RelayCommand(CanExecute = nameof(CanLaunchMission))]
private async Task LaunchMissionAsync(CancellationToken cancellationToken) { ... }
```

`[RelayCommand]` génère `LaunchMissionCommand`, que le bouton de la vue utilise. Tant que le but est vide, la commande est désactivée, et le bouton grisé.

### 3.3 Une erreur réseau ne fait pas planter l'écran

```csharp
catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                           || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
{
    Error = ex.Message;
}
```

API éteinte, réponse illisible, délai dépassé : le message s'affiche sous le champ, l'application continue. On retrouve le même filtre sur le jeton qu'à la leçon 13.

### 3.4 Nouveau : la liste des missions

```csharp
public async Task<IReadOnlyList<MissionDto>> ListMissionsAsync(int limit = 50, ...) =>
    await http.GetFromJsonAsync<MissionDto[]>($"missions?limit={limit}", cancellationToken) ?? [];
```

```csharp
public async Task LoadAsync(CancellationToken cancellationToken = default)
```

Le squelette n'affichait que les missions lancées **depuis la fenêtre ouverte**. Maintenant que l'API expose `GET /missions` (leçon 18), l'accueil charge **toutes** les missions récentes, y compris celles lancées depuis le téléphone.

### 3.5 Nouveau : le statut « À valider »

```csharp
public bool IsAwaitingApproval => Status == MissionStatus.AwaitingApproval;

MissionStatus.AwaitingApproval => "À valider",
```

Une mission qui attend ton accord (leçon 20) s'affichait « Échec ». Elle a maintenant son libellé et sa propriété, que la vue colorera en **laiton**, la couleur des décisions humaines.

### 3.6 Le temps réel

```csharp
_connection = new HubConnectionBuilder()
    .WithUrl(new Uri(apiBaseAddress, MissionsHubProtocol.Path), ...)
    .WithAutomaticReconnect()
    .Build();
_connection.On<MissionEventDto>(MissionsHubProtocol.MissionEvent, e => MissionEventReceived?.Invoke(e));
```

`MissionsHubClient` écoute le hub de la leçon 15. `WithAutomaticReconnect()` : après une coupure réseau, la connexion se rétablit toute seule. Le ViewModel applique chaque événement :

```csharp
public void Apply(MissionEventDto e)
{
    var mission = Missions.FirstOrDefault(m => m.Id == e.MissionId);
    if (mission is not null)
        mission.Status = e.Status;
}
```

## 4. Les tests, sans écran ni réseau

```csharp
private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
```

Un **faux réseau** : `HttpClient` passe par ce gestionnaire, qui renvoie la réponse qu'on veut. On vérifie la requête envoyée **et** l'effet sur le ViewModel.

| Test | Vérifie |
|---|---|
| `Launch_is_disabled_while_the_goal_is_blank` | Bouton désactivé tant que le but est vide |
| `Launch_posts_the_goal_and_shows_the_created_mission` | `POST /missions`, la carte apparaît, un événement la passe à « Terminée » |
| `Launch_shows_an_error_when_the_api_fails` | Une erreur 500 s'affiche, sans planter |
| `Load_shows_the_missions_returned_by_the_api` | `GET /missions?limit=50`, les cartes, le libellé « À valider » |

## 5. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Client.Core.Tests
```

## 6. Quiz

1. Quels sont les trois rôles du motif MVVM ?
2. Pourquoi peut-on tester un ViewModel sans écran ?
3. Que génère `[RelayCommand]` ?
4. Pourquoi charger la liste au démarrage plutôt que garder seulement les missions lancées depuis la fenêtre ?

## 7. Pour aller plus loin

- CommunityToolkit.Mvvm : <https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/>
- Le client SignalR .NET : <https://learn.microsoft.com/aspnet/core/signalr/dotnet-client>

<details>
<summary>Réponses du quiz</summary>

1. Le Model (les données), le ViewModel (l'état et les actions de l'écran), la View (l'affichage).
2. Parce qu'il ne dépend d'aucune technologie d'affichage : ce n'est que du C#.
3. Une commande (`LaunchMissionCommand`) que la vue peut lier à un bouton, avec sa condition d'activation.
4. Pour voir aussi les missions lancées ailleurs (téléphone, autre poste) ou avant le redémarrage de l'application.

</details>
