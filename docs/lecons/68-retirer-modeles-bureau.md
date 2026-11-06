# Leçon 68 — Supprimer du code : l'écran Modèles quitte le bureau

> **Commit** : `refactor(desktop): retirer la gestion des modèles du bureau`
> **Phase** : compléter l'admin
> **Fichiers** : `Client.Core/` (`ShellViewModel.cs`, `ApiClient.cs`, `SettingsViewModel.cs` supprimé), `Desktop/` (`MainWindow.axaml`, `App.axaml.cs`, `Views/SettingsView.axaml` supprimé), tests (`SettingsViewModelTests.cs` supprimé, `MissionViewModelTests.cs`)

## 1. Ce qu'on fait

L'ADR 0008 l'annonçait : la gestion se fait dans l'**espace admin web**. Le bureau perd donc son écran Paramètres › Modèles, son ViewModel, ses huit tests, et six méthodes de l'`ApiClient` qui ne servaient qu'à lui.

À la place, un administrateur voit un bouton **Ouvrir l'espace admin**, qui ouvre le navigateur du système (`AGENTYATTUS_ADMIN_URL`, par défaut `http://localhost:4200`).

## 2. Supprimer, c'est aussi du travail

Supprimer du code qui marche fait peur : il a coûté des heures (leçons 33 à 36). Mais du code gardé « au cas où » coûte chaque jour : il faut le compiler, le tester, le mettre à jour quand l'API change, et deux écrans qui font la même chose finissent par diverger. Le code n'est pas perdu : il reste dans l'historique Git, et la leçon 33 l'explique.

On supprime **tout** ce qui ne sert plus, pas seulement l'écran :

- `SettingsView.axaml` et `SettingsViewModel.cs` ;
- les méthodes de l'`ApiClient` (`ConnectLlmAsync`, `UpdateFreeModeAsync`…) ;
- leurs tests.

Le compilateur aide : après la suppression, une erreur signale chaque endroit qui s'en servait encore.

## 3. Le ViewModel ne connaît pas le navigateur

```csharp
public event Action<Uri>? OpenUrlRequested;

[RelayCommand]
private void OpenAdmin() => OpenUrlRequested?.Invoke(AdminUrl);
```

`ShellViewModel` vit dans `Client.Core`, sans interface graphique (ADR 0001). Il ne lance pas de navigateur : il **demande** qu'on ouvre une adresse. C'est l'application de bureau qui répond, avec `Process.Start`. Le test vérifie la demande sans rien ouvrir.

## 4. Quiz

1. Pourquoi supprimer un écran qui fonctionne ?
2. Pourquoi `ShellViewModel` émet-il un événement au lieu d'ouvrir le navigateur lui-même ?

<details>
<summary>Réponses du quiz</summary>

1. Il fait doublon avec l'admin web ; le garder coûte de la maintenance et les deux finiraient par diverger. Il reste dans l'historique Git.
2. `Client.Core` n'a pas d'interface graphique : il exprime une intention, et l'application décide comment l'exécuter. Le test reste simple.

</details>
