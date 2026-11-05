# Leçon 24 — L'écran « Mission en direct »

> **Commit** : `feat(desktop): écran Mission en direct`
> **Phase** : 3, client desktop
> **Fichiers** : API (`AuditModule.cs`, `MissionsModule.cs`, `Contracts/Missions.cs`), Client.Core (`ShellViewModel.cs`, `MissionViewModel.cs`, `MissionItem.cs`, `HomeViewModel.cs`, `ApiClient.cs`), Desktop (`MainWindow.axaml`, `Views/MissionView.axaml`, `Views/HomeView.axaml`, `Theme/Controls.axaml`), tests

## 1. Ce qu'on construit

Un clic sur une carte de l'accueil ouvre la mission :

```
  ◎ Objectif ── ⟁ Analyste ── ◆ Vous ── ▤ Rapport
                 list_files 2
                 read_file 4
  ───────────────────────────────────────────────
  Journal 9   •───•───•───•───•───◆───•   En attente de décision 22:49
```

- **Le déroulement** : l'objectif, l'agent (il **pulse** tant qu'il travaille, avec les outils qu'il a utilisés), **toi** (losange laiton qui pulse quand ta décision est attendue, éteint si aucune décision n'a été nécessaire), le rapport (en pointillés tant qu'il n'existe pas).
- **La réponse** de l'agent, une fois la mission terminée.
- **Le journal** : une marque par événement. Point vert pour une action exécutée, losange laiton pour une décision, croix cinabre pour un échec.

## 2. Côté API : lire le journal

```csharp
missions.MapGet("/{id:guid}/events", GetEventsAsync);
```

```csharp
public async Task<AuditEntryDto[]> ReadAsync(Guid missionId, CancellationToken cancellationToken) =>
    await _db.Events.Where(e => e.MissionId == missionId).OrderBy(e => e.Id)
        .Select(e => new AuditEntryDto(e.Type, e.At)).ToArrayAsync(cancellationToken);
```

Le module Audit **expose** la lecture par une méthode publique de `AuditLog`. Le module Missions branche la route. Ses tables restent privées (ADR 0003).

`.Select(...)` **avant** `ToArrayAsync` : EF Core ne lit en base que les deux colonnes utiles.

## 3. La navigation : une coquille et des écrans

```csharp
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ObservableObject Current { get; set; }
```

La fenêtre affiche « **l'écran courant** », un ViewModel. Chaque type de ViewModel a sa vue :

```xml
<Window.DataTemplates>
  <DataTemplate DataType="core:HomeViewModel"><views:HomeView /></DataTemplate>
  <DataTemplate DataType="core:MissionViewModel"><views:MissionView /></DataTemplate>
</Window.DataTemplates>
<ContentControl Content="{Binding Current}" />
```

Naviguer, c'est **changer** `Current`. Avalonia choisit la vue tout seul.

C'est la **navigation pilotée par les ViewModels** : la logique (quel écran, quand) est testable sans interface, et le mobile pourra réutiliser `ShellViewModel`.

L'en-tête et la barre d'état passent dans la fenêtre : ils ne changent pas d'un écran à l'autre. Les styles communs passent dans `Theme/Controls.axaml`.

### L'accueil ne connaît pas la navigation

```csharp
public event Action<MissionItem>? MissionOpened;

[RelayCommand]
private void OpenMission(MissionItem mission) => MissionOpened?.Invoke(mission);
```

L'accueil **annonce** « on veut ouvrir cette mission ». La coquille écoute et décide. Chaque ViewModel garde un seul rôle.

## 4. L'état de la mission — `MissionViewModel`

```csharp
foreach (var group in events.Select(e => e.Type).Where(t => t.StartsWith("outil:", StringComparison.Ordinal))
             .GroupBy(t => t["outil:".Length..]))
    Tools.Add(new ToolUsage(group.Key, group.Count()));
```

Les outils sont **comptés** à partir du journal : `outil:read_file` ×4 donne la pastille « read_file 4 ».

```csharp
public sealed record TimelineEntry(string Label, DateTimeOffset At, bool IsTool, bool IsDecision, bool IsFailure)
```

Chaque type d'événement d'audit devient un **libellé en français** et un **genre de marque**. La vue dessine selon le genre, sans connaître les noms techniques.

### Le direct

```csharp
public Task ApplyAsync(MissionEventDto e) => e.MissionId == Mission.Id ? LoadAsync() : Task.CompletedTask;
```

À chaque changement de statut de **cette** mission, l'écran recharge la mission et son journal.

```csharp
// ponytail: rechargement à chaque changement de statut ; les appels d'outils pendant un même tour
// apparaissent à la fin de ce tour.
```

**Limite assumée** : SignalR ne pousse que les changements de **statut**. Les outils appelés pendant que l'agent travaille apparaissent donc quand il change d'état (pause, fin). Pour un direct outil par outil, il faudrait pousser chaque événement d'audit par le hub.

## 5. La vue — `MissionView.axaml`

Quelques techniques Avalonia utilisées :

```xml
<Ellipse Classes="pulse" ... IsVisible="{Binding Mission.IsActive}" />
```

Le halo n'existe que pendant le travail de l'agent.

```xml
IsVisible="{Binding Result, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
```

Un **convertisseur** fourni par Avalonia : visible seulement si la réponse existe.

```xml
<ItemsControl ItemsSource="{Binding Journal}">
  <ItemsControl.ItemsPanel><ItemsPanelTemplate><UniformGrid Rows="1" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
```

Le journal : une ligne, des marques **réparties à égale distance**.

```xml
<SelectableTextBlock Text="{Binding Result}" TextWrapping="Wrap" />
```

La réponse est **sélectionnable** : on peut la copier.

## 6. Tests et rendu

| Test | Vérifie |
|---|---|
| `Load_counts_the_tools_used_and_reads_the_journal` | `list_files 1`, `read_file 2`, 5 entrées, « Terminée », nœud « Vous » éteint |
| `Load_shows_the_human_decision` | Décision approuvée, nœud « Vous » actif, marque « Écriture approuvée » |
| `Hub_events_reload_only_our_mission` | Un événement d'une autre mission ne déclenche aucun appel |
| `Opening_a_mission_from_home_shows_its_screen` | Clic sur une carte → écran Mission ; retour → accueil |

Les deux états (en attente de décision, terminée) ont été rendus en PNG et comparés à la maquette. Trois retouches en sont sorties : centrage des onglets, libellé de l'agent (« en pause » plutôt que « À valider »), nœud « Vous » éteint quand aucune décision n'a été demandée.

## 7. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Client.Core.Tests
```

```bash
dotnet run --project src/AgentYattus.Desktop
```

## 8. Quiz

1. Comment la fenêtre sait-elle quelle vue afficher pour l'écran courant ?
2. Pourquoi l'accueil annonce-t-il un événement plutôt que de naviguer lui-même ?
3. D'où viennent les compteurs d'outils sous l'agent ?
4. Pourquoi les outils n'apparaissent-ils pas un par un pendant que l'agent travaille ?

## 9. Pour aller plus loin

- Les data templates d'Avalonia : <https://docs.avaloniaui.net/docs/basics/data/data-templates>
- Convertisseurs intégrés : <https://docs.avaloniaui.net/docs/basics/data/data-binding/converters>

<details>
<summary>Réponses du quiz</summary>

1. Par les `DataTemplates` de la fenêtre : à chaque type de ViewModel correspond une vue.
2. Pour que chaque ViewModel garde un seul rôle : la navigation appartient à la coquille, et reste testable.
3. Du journal d'audit : chaque `outil:<nom>` est compté.
4. Parce que le hub ne pousse que les changements de statut ; l'écran recharge le journal à ces moments-là.

</details>
