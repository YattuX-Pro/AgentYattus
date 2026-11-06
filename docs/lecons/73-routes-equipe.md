# Leçon 73 — L'équipe d'un projet : un siège par rôle

> **Commit** : `feat(team): équipes des projets, traits, répliques et réglages`
> **Phase** : le bureau des agents
> **Fichiers** : `Api/Modules/Team/` (`Colleagues.cs`, `TeamContent.cs`), `Agents/AgentRoles.cs` (`AgentCatalog`), `tests/…/TeamTests.cs`

## 1. Ce qu'on construit

- `GET /projects/{id}/team` : tout ce que le bureau doit savoir pour jouer la scène, **en un appel** (collègues, chef d'équipe, répliques, vitesse, droit de recruter).
- `POST /projects/{id}/colleagues` : recruter. `DELETE …/{colleague}` : retirer.
- `GET /team/colleagues` : l'historique, pour la page Équipes de l'admin.
- Traits, répliques et réglages : lus par tous, modifiés par les administrateurs.

## 2. Un siège par rôle, garanti par la base

```csharp
e.HasIndex(c => new { c.ProjectId, c.Role }, "IX_Colleagues_Seat").IsUnique().HasFilter("\"Active\"");
```

Un **index unique partiel** : un seul collègue **actif** par rôle et par projet. Recruter libère d'abord l'ancien siège, puis installe le nouveau, en deux `SaveChanges`. Si deux recrutements arrivent en même temps, la base en refuse un, au lieu de laisser deux analystes.

## 3. Une valeur par défaut, puis ce que l'admin a choisi

Comme les réglages (leçon 53) : une réplique sans ligne en base garde sa **réplique de départ**. Effacer une réplique dans l'admin revient au départ, sans rien perdre. Les traits fonctionnent de même : tant que la table est vide, on propose les cinq traits de départ.

Le code décide **qui parle** pour chaque événement (le comportement) ; l'admin décide **ce qu'il dit** (le contenu).

## 4. Une variable inconnue est refusée

`Je lis {chemin}.` est refusé, avec la liste des variables disponibles. Une faute de frappe se voit à l'enregistrement, pas dans une bulle vide pendant une mission.

## 5. L'équipe de départ, installée une seule fois

La première lecture de l'équipe d'un projet installe l'équipe de départ (réglée dans l'admin). Un projet dont on a retiré tous les collègues n'en reçoit pas une nouvelle : il a **déjà** eu une équipe, et on respecte ce choix.

## 6. Quiz

1. Qu'empêche l'index unique partiel ?
2. Pourquoi refuser une variable inconnue dès l'enregistrement ?

<details>
<summary>Réponses du quiz</summary>

1. Deux collègues actifs pour le même rôle dans un projet, même si deux recrutements arrivent en même temps.
2. Pour voir l'erreur tout de suite dans l'admin, plutôt qu'une bulle incomplète pendant une mission.

</details>
