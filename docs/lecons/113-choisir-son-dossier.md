# Leçon 113 — Choisir le dossier depuis l'accueil

> **Commit** : `feat(desktop): choisir le dossier d'un projet depuis le logiciel`
> **Phase** : A
> **Fichiers** : `Client.Core/{HomeViewModel,ApiClient}.cs`, `Desktop/Views/HomeView.axaml(.cs)`, `tests/.../HomeViewModelTests.cs`

## 1. Ce qu'on construit

Dans la barre du composeur, à côté du projet, un bouton dossier : « orders-api », ou « Choisir un dossier ». Un clic ouvre le **sélecteur de dossiers du système**. Le dossier choisi est envoyé à l'API, qui vérifie les règles de la leçon 112.

Le bouton n'apparaît que pour celui qui gère le projet, et seulement s'il a un endroit où choisir.

## 2. Le ViewModel ne connaît pas l'interface

`HomeViewModel` vit dans `Client.Core`, sans Avalonia : il ne peut pas ouvrir de fenêtre. Il expose une simple fonction, que la vue lui donne :

```csharp
public Func<string?, Task<string?>>? PickFolder { get; set; }
```

`HomeView` la remplit avec `StorageProvider.OpenFolderPickerAsync`. Dans les tests, c'est une fonction qui rend un dossier temporaire. Le ViewModel reste testable sans écran.

## 3. Les limites

- En mode serveur, le sélecteur montre les dossiers de **ta** machine. C'est juste si le serveur tourne sur la même machine, ou voit le même partage. Sinon, l'API répond que le dossier n'existe pas sur le serveur.
- Le bouton reprend les composants du composeur. Il n'a pas d'artboard dans la maquette.

## 4. Quiz

1. Pourquoi passer une fonction plutôt qu'appeler le sélecteur depuis le ViewModel ?

<details>
<summary>Réponse</summary>

1. Le ViewModel ne dépend pas d'Avalonia : il se teste sans interface, et pourrait servir au mobile avec un autre sélecteur.

</details>
