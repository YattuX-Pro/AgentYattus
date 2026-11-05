# Leçon 26 — Les écrans Agents et Rapports

> **Commit** : `feat(desktop): écrans Agents et Rapports`
> **Phase** : 3, client desktop
> **Fichiers** : API (`Modules/Agents/AgentsModule.cs`, `WorkspaceTools.cs`, `Contracts/Agents.cs`, `Program.cs`), Client.Core (`AgentsViewModel.cs`, `ReportsViewModel.cs`, `ShellViewModel.cs`, `ApiClient.cs`), Desktop (`Views/AgentsView.axaml`, `Views/ReportsView.axaml`, `MainWindow.axaml`), tests

## 1. Ce qu'on construit

Les deux derniers onglets de la navigation :

- **Agents** : une carte par agent (nom, modèle, limite de tours) et un **inspecteur** qui liste ses outils. Les outils de lecture sont en vert ; ceux qui demandent ton accord, en **laiton**, avec la mention « sensible · approbation ».
- **Rapports** : les missions terminées. Pour chacune, **ce qui a été exécuté** (lectures, écritures approuvées, refus) et la réponse de l'agent.

## 2. Une seule source de vérité pour les outils

L'écran Agents doit afficher les outils **sans** avoir de workspace sous la main. On décrit donc les outils dans un catalogue statique :

```csharp
public static IReadOnlyList<(string Name, string Description, bool RequiresApproval)> Catalog { get; } =
[
    ("list_files", "Liste les fichiers et dossiers d'un dossier du workspace.", false),
    ("read_file", "Lit le contenu texte d'un fichier du workspace.", false),
    ("write_file", "Crée ou remplace un fichier texte du workspace. Un humain doit approuver chaque écriture.", true),
];
```

…et les vrais outils **lisent** ce catalogue (nom, description). Le risque d'une telle duplication, c'est que l'écran **mente** : afficher « lecture » pour un outil qui écrit. Un test l'empêche :

```csharp
Assert.Equal(WorkspaceTools.Catalog.Select(t => (t.Name, t.RequiresApproval)),
    tools.Select(t => (t.Name, t is ApprovalRequiredAIFunction)));
```

Si quelqu'un ajoute un outil sensible sans le déclarer, ce test échoue.

## 3. `GET /agents`

```csharp
app.MapGet(AgentsProtocol.Path, (IOptions<AgentsOptions> options) => ...);
```

La route renvoie le modèle et la limite de tours **configurés** (leçon 17). Ce qu'affiche l'écran est donc bien ce qui tourne. Le chemin de la route est une constante partagée dans `Contracts`, comme pour le hub (leçon 15).

Pour l'instant, il n'y a qu'un agent, l'Analyste. Le Rédacteur arrive à la phase 4.

## 4. Les rapports : des chiffres qui viennent du journal

```csharp
// Les chiffres viennent du journal d'audit : ce qui a réellement été exécuté, pas ce que l'agent en dit.
partial void OnSelectedChanged(MissionDto? value) => _ = LoadStatsAsync(value);
```

- `partial void On<Propriété>Changed` : un « crochet » généré par CommunityToolkit.Mvvm, appelé à chaque changement de sélection.
- Les chiffres sont comptés dans le **journal d'audit**, écrit par le backend, pas dans la réponse de l'agent. Un agent pourrait écrire « j'ai lu 50 fichiers » ; le journal, lui, ne ment pas. C'est le sens du titre « EXÉCUTÉ · SOURCE BACKEND » de la maquette.

```csharp
if (Selected?.Id == report.Id)
    Stats = new ReportStats(...);
```

Si tu cliques vite sur deux rapports, la réponse du premier peut arriver **après** celle du second. On vérifie que la sélection n'a pas changé avant d'afficher les chiffres : c'est une **condition de course** évitée.

## 5. La vue : une liste sélectionnable

```xml
<ListBox Classes="agents" ItemsSource="{Binding Agents}" SelectedItem="{Binding Selected}">
  <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate></ListBox.ItemsPanel>
```

Une `ListBox` donne la sélection, au clavier comme à la souris. Le `WrapPanel` dispose les cartes en grille qui passe à la ligne. Le style retire l'habillage par défaut, et colore le contour de la carte sélectionnée.

## 6. Tests

| Test | Vérifie |
|---|---|
| `Agents_lists_the_analyst_with_its_tools_and_flags_sensitive_ones` (API) | `GET /agents` : l'Analyste, ses 3 outils, `write_file` seul sensible |
| `The_catalog_shown_to_users_matches_the_real_tools` (API) | Le catalogue correspond aux vrais outils |
| `Agents_screen_selects_the_first_agent` | Le premier agent est sélectionné |
| `Reports_keep_completed_missions_and_count_what_was_executed` | Seules les missions terminées ; 2 lectures, 1 approuvée, 1 refus |

Les deux écrans ont été rendus et comparés à la maquette.

## 7. Quiz

1. Pourquoi un test compare-t-il le catalogue aux vrais outils ?
2. D'où viennent les chiffres d'un rapport, et pourquoi pas de la réponse de l'agent ?
3. Quelle condition de course la vérification `Selected?.Id == report.Id` évite-t-elle ?

## 8. Pour aller plus loin

- Les méthodes partielles générées par CommunityToolkit.Mvvm : <https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/observableproperty>
- ListBox dans Avalonia : <https://docs.avaloniaui.net/docs/reference/controls/listbox>

<details>
<summary>Réponses du quiz</summary>

1. Pour que l'écran ne puisse pas afficher « lecture » pour un outil qui demande en réalité une approbation.
2. Du journal d'audit, écrit par le backend : il reflète ce qui a réellement été exécuté, alors que l'agent pourrait se tromper ou exagérer.
3. Si deux rapports sont sélectionnés vite, la réponse du premier ne doit pas s'afficher sur le second.

</details>
