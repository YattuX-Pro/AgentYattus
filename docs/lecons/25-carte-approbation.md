# Leçon 25 — La carte d'approbation

> **Commit** : `feat(desktop): carte d'approbation`
> **Phase** : 3, client desktop
> **Fichiers** : `src/AgentYattus.Client.Core/` (`ApiClient.cs`, `MissionViewModel.cs`), `src/AgentYattus.Desktop/Views/MissionView.axaml`, `tests/AgentYattus.Client.Core.Tests/MissionViewModelTests.cs`

## 1. Ce qu'on construit

Quand l'agent veut écrire un fichier, une **carte flottante** s'affiche à droite de l'écran Mission :

```
┌────────────────────────────────────┐
│ ◆ write_file                       │
│ Écrire REPORT.md ?                 │
│ docs/REPORT.md                     │
│ ┌────────────────────────────────┐ │
│ │ + # Revue de performance       │ │
│ │ + ## Constats (3)              │ │
│ │ + 1. Appel .Result bloquant…   │ │
│ │   +4 lignes                    │ │
│ └────────────────────────────────┘ │
│  [ Refuser ]   [   Approuver   ]   │
└────────────────────────────────────┘
```

C'est l'écran qui tient la promesse de la leçon 20 : **tu vois exactement ce qui va être fait, et tu décides.**

## 2. Les principes d'une bonne demande d'approbation

1. **Montrer l'action réelle, pas un résumé.** La carte affiche le chemin et le contenu tels que l'outil les recevra. Un agent pourrait annoncer « j'écris un rapport » et écrire autre chose : seuls les arguments réels comptent.
2. **Décider en quelques secondes.** Un titre court (« Écrire REPORT.md ? »), le début du contenu, le nombre de lignes restantes.
3. **Approuver doit se distinguer de refuser.** Approuver est en **laiton**, la couleur des décisions humaines, et plus large. Refuser est en contour.
4. **Tout montrer.** Si l'agent demande plusieurs actions d'un coup, la carte les liste **toutes** avant les boutons (leçon 20 : une décision vaut pour toutes).

## 3. Le code expliqué

### 3.1 Transformer l'appel d'outil en carte — `ApprovalItem`

```csharp
public static ApprovalItem From(ToolCallDto call)
{
    ...
    if (call.Tool == "write_file" && Text("path") is { } path)
    {
        var lines = (Text("content") ?? "").Split('\n');
        return new ApprovalItem(call.Tool, $"Écrire {Path.GetFileName(path)} ?", path,
            [.. lines.Take(PreviewLines)], Math.Max(0, lines.Length - PreviewLines));
    }

    return new ApprovalItem(call.Tool, $"Autoriser {call.Tool} ?", null, [call.Arguments], 0);
}
```

- Les arguments arrivent en JSON (leçon 20). On en extrait `path` et `content` pour `write_file`.
- **Un outil inconnu n'est jamais caché** : on affiche ses arguments bruts. Si le JSON est illisible, pareil. Mieux vaut un affichage moins joli qu'une décision à l'aveugle.

### 3.2 Les commandes Approuver et Refuser

```csharp
[RelayCommand(CanExecute = nameof(IsDecisionPending))]
private Task ApproveAsync(CancellationToken cancellationToken) => DecideAsync(true, cancellationToken);
```

Les boutons ne sont actifs que si une décision est **réellement** attendue.

```csharp
public async Task<bool> DecideAsync(Guid missionId, bool approved, CancellationToken cancellationToken = default)
{
    using var response = await http.PostAsJsonAsync($"missions/{missionId}/decision", new DecisionRequest(approved), cancellationToken);
    if (response.StatusCode == HttpStatusCode.Conflict)
        return false;
    ...
}
```

Un **409** n'est pas une panne : il veut dire que la décision a déjà été prise, par exemple depuis le téléphone. On l'explique à l'utilisateur au lieu d'afficher une erreur technique.

### 3.3 Un bug trouvé par un test

Première version :

```csharp
if (!await api.DecideAsync(...))
    Error = "Cette décision a déjà été prise…";
await LoadAsync(cancellationToken);   // ← remet Error à null !
```

Le message s'affichait… puis disparaissait aussitôt, effacé par le rechargement. Le test `A_decision_already_taken_elsewhere_is_explained` l'a détecté. Correction : afficher le message **après** le rechargement.

C'est exactement à ça que servent les tests de ViewModel : ce bug aurait été très difficile à voir à l'œil, car le message ne restait affiché qu'une fraction de seconde.

## 4. La vue

```xml
<Border ... BorderBrush="#73D8B86E" BoxShadow="0 30 90 0 #8C000000, 0 0 0 6 #0DD8B86E"
        IsVisible="{Binding IsDecisionPending}">
```

Une ombre profonde pour faire **flotter** la carte, et un halo laiton très léger.

```xml
<TextBlock Classes="mono" Text="+ " Foreground="{StaticResource VertDeGris}" />
<TextBlock Classes="mono" Text="{Binding}" ... TextTrimming="CharacterEllipsis" />
```

Le contenu est présenté comme un **diff** : chaque ligne commence par un `+` vert, ce qui veut dire « ajouté ». C'est un code visuel que tout développeur reconnaît.

## 5. Tests

| Test | Vérifie |
|---|---|
| `Approving_posts_the_decision_and_shows_exactly_what_will_be_written` | Titre, chemin complet, 6 lignes d'aperçu, « +4 lignes », puis `POST …/decision` avec `"approved":true` |
| `A_decision_already_taken_elsewhere_is_explained` | Un 409 affiche « Cette décision a déjà été prise… » |

## 6. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Client.Core.Tests
```

Avec une vraie clé : lance « Écris un fichier NOTES.md qui résume le README », ouvre la mission, et décide.

## 7. Quiz

1. Pourquoi afficher les arguments réels de l'outil plutôt qu'une description donnée par l'agent ?
2. Que fait la carte pour un outil qu'elle ne connaît pas ?
3. Que signifie un 409 sur la décision, et comment l'interface le présente-t-elle ?
4. Pourquoi le message d'erreur disparaissait-il dans la première version ?

## 8. Pour aller plus loin

- Building effective agents (Anthropic) : <https://www.anthropic.com/engineering/building-effective-agents>
- OWASP Top 10 LLM, « Excessive Agency » : <https://genai.owasp.org/llm-top-10/>

<details>
<summary>Réponses du quiz</summary>

1. Parce que la description peut être fausse ou manipulée ; seuls les arguments seront réellement exécutés.
2. Elle affiche ses arguments bruts, pour ne jamais faire décider à l'aveugle.
3. Que la décision a déjà été prise (par exemple depuis un autre appareil) ; l'interface l'explique en clair.
4. Parce que le rechargement qui suivait remettait l'erreur à `null`.

</details>
