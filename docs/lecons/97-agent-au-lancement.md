# Leçon 97 — Choisir l'agent au lancement, et lui donner un bureau

> **Commit** : `feat(desktop): choisir l'agent d'une mission`
> **Phase** : le bureau des agents
> **Fichiers** : `Client.Core/` (`HomeViewModel.cs`, `ApiClient.cs`, `MissionItem.cs`, `Scene.cs`, `OfficeViewModel.cs`), `Desktop/Views/HomeView.axaml`, tests

## 1. Ce qu'on construit

Dans le compositeur de l'accueil, à côté du projet : la liste des agents. Une question part avec l'agent choisi (par défaut l'Analyste) ; quand la bascule « REPORT.md » est active, la liste se grise : une revue suit toujours le circuit Analyste puis Rédacteur.

Dans l'open space, l'agent choisi s'installe au **bureau de l'analyste** : son visage et son prénom (le collègue recruté pour ce rôle), son rôle sur la plaque, ses bulles, et c'est lui qui va chercher le dossier aux archives.

## 2. Un paramètre, pas une copie

`Scene.From(…, worker: "testeur")` : la scène ne connaît pas les agents créés, elle sait seulement **qui occupe le bureau de l'analyste**. `OfficeViewModel.DeskOf` fait parler cet agent depuis ce bureau. Aucun nouvel écran, aucun nouveau bureau à placer.

## 3. Une liste qui peut manquer

Comme les projets (leçon 69), les agents se chargent dans leur propre bloc : sans eux (API plus ancienne, coupure), la mission part avec l'Analyste. Une donnée secondaire ne fait jamais tomber l'écran.

## 4. Quiz

1. Pourquoi la liste des agents se grise-t-elle pour une revue ?
2. Comment la scène fait-elle parler un agent qu'elle ne connaît pas ?

<details>
<summary>Réponses du quiz</summary>

1. Une revue suit un circuit fixé (Analyste puis Rédacteur) ; l'agent ne se choisit que pour une question (ADR 0013).
2. On lui dit quel rôle occupe le bureau de l'analyste (worker) ; les répliques de ce rôle s'affichent depuis ce bureau.

</details>
