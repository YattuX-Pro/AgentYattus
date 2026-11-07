# Leçon 90 — Recruter : un ViewModel qui suggère sans imposer

> **Commit** : `feat(client): RecruitViewModel`
> **Phase** : le bureau des agents
> **Fichiers** : `Client.Core/RecruitViewModel.cs`, `tests/…/RecruitViewModelTests.cs`

## 1. Ce qu'on construit

Le ViewModel de l'écran « Nouveau collègue » : la galerie (visages actifs), les traits et les postes (tous deux venus de l'API, donc de l'admin), le prénom, et l'aperçu de la première bulle.

## 2. Suggérer sans écraser

Choisir un visage propose son prénom suggéré, **sauf** si l'on a déjà tapé un autre prénom :

```csharp
if (string.IsNullOrWhiteSpace(Name) || Name == suggested)
    Name = choice.Portrait.SuggestedName;
```

Une suggestion qui efface ce que l'utilisateur a écrit est une petite trahison ; celle-ci ne remplace que sa propre suggestion.

## 3. La même règle que le serveur

Le bouton « Recruter » reste grisé tant que le prénom n'est pas valide, avec la règle du serveur (des lettres seulement, leçon 72). Le serveur vérifie de toute façon ; l'écran évite simplement un aller-retour pour rien.

## 4. Les outils ne se choisissent pas ici

Le poste affiche les outils et le modèle du rôle, en lecture seule : ils se règlent dans l'admin (ADR 0012). Recruter ne peut pas donner `write_file` à une analyste.

## 5. Quiz

1. Pourquoi ne pas remplacer le prénom tapé quand on change de visage ?
2. Pourquoi l'écran vérifie-t-il le prénom alors que le serveur le fait déjà ?

<details>
<summary>Réponses du quiz</summary>

1. Ce serait effacer le choix de l'utilisateur ; on ne remplace que la suggestion précédente.
2. Pour griser le bouton tout de suite, sans aller-retour ; le serveur reste la vraie barrière.

</details>
