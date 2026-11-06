# Leçon 69 — Choisir le projet d'une mission depuis le bureau

> **Commit** : `feat(desktop): choisir le projet d'une mission`
> **Phase** : compléter l'admin
> **Fichiers** : `Client.Core/` (`ApiClient.cs`, `HomeViewModel.cs`), `Desktop/Views/HomeView.axaml`, `tests/…/HomeViewModelTests.cs`

## 1. Ce qu'on construit

Dans le compositeur de l'accueil, à côté de la bascule REPORT.md : une liste des **projets où l'on peut lancer une mission** (propriétaire ou éditeur). La mission part dans le projet choisi ; sans choix, dans le projet personnel.

## 2. Proposer seulement ce qui marchera

```csharp
foreach (var project in (await api.GetProjectsAsync(cancellationToken)).Where(p => p.MyAccess is ProjectRole.Owner or ProjectRole.Editor))
    Projects.Add(project);
```

Un lecteur voit les missions d'un projet mais n'y lance rien : le serveur répondrait 403 (leçon 48). On ne le propose donc pas. C'est le même principe que l'admin (leçon 49) : l'écran évite l'erreur, le serveur garde la règle.

Le choix par défaut : le projet déjà choisi s'il existe encore, sinon le projet personnel, sinon le premier.

## 3. Une panne partielle reste partielle

```csharp
// Sans la liste des projets (API plus ancienne, coupure), on lance quand même : dans le projet personnel.
catch (...) { Projects.Clear(); SelectedProject = null; }
```

Les missions et les projets se chargent dans **deux blocs séparés**. Si la liste des projets ne répond pas, les missions s'affichent quand même, et le lancement fonctionne (le serveur choisit le projet personnel). Un écran qui tombe entièrement pour une donnée secondaire est un écran fragile.

## 4. Quiz

1. Pourquoi ne pas proposer les projets où l'on est lecteur ?
2. Pourquoi charger les projets dans un bloc à part ?

<details>
<summary>Réponses du quiz</summary>

1. Le serveur refuserait la mission (403) : on ne propose pas un choix voué à l'échec.
2. Pour qu'une erreur sur les projets n'empêche ni d'afficher les missions ni d'en lancer une.

</details>
