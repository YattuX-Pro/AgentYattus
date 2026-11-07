# Leçon 120 — La bulle s'écrit en direct

> **Commit** : `feat(desktop): la réponse s'écrit en direct, dans Rapports et l'open space`
> **Phase** : B
> **Fichiers** : `Client.Core/{MissionsHubClient,ShellViewModel,ReportsViewModel,OfficeViewModel}.cs`, `Desktop/App.axaml.cs`, `Desktop/Views/OfficeView.axaml`, `tests/.../ReplyTests.cs`

## 1. Ce qu'on construit

- **Rapports** : après « Répondre », la bulle « écrit… » se remplit, morceau par morceau.
- **Open space** : en bas du fil de l'équipe, une carte « RÉPONSE DE INÈS » s'allonge pendant que l'agent écrit. Elle reste une fois la mission finie : c'est sa réponse. Elle repart de zéro à la prochaine exécution (relance, réponse).

## 2. Le trajet d'un morceau

```
API (MissionText) → MissionsHubClient.MissionTextReceived → fil de l'interface (Dispatcher)
  → ShellViewModel.ApplyText → l'écran affiché (Rapports ou open space)
```

Un morceau qui concerne une autre mission est ignoré. Dans Rapports, seulement pendant l'attente d'une réponse.

## 3. Pourquoi `Thread[^1] = last with { Text = … }` ?

`ReportMessage` est un *record* immuable. Pour changer le texte, on remplace l'élément de la liste par une copie modifiée. L'`ObservableCollection` prévient la vue, qui redessine cette seule bulle.

## 4. Vérifie toi-même

`dotnet test --filter ReplyTests` : deux morceaux remplissent la bulle, un morceau d'une autre mission est ignoré.

## 5. Les limites

- La carte de l'open space reprend les composants du fil, sans artboard dans la maquette.
- Rien ne relie la carte à la bulle de la scène : celle-ci joue toujours les répliques du journal.

## 6. Quiz

1. Pourquoi passer par `Dispatcher.UIThread.Post` ?

<details>
<summary>Réponse</summary>

1. SignalR appelle le gestionnaire sur un fil de fond. Les objets liés à l'interface ne se modifient que sur le fil de l'interface.

</details>
