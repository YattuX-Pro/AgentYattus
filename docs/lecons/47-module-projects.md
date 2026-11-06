# Leçon 47 — Le module Projects : une règle d'accès, écrite deux fois

> **Commit** : `feat(projects): projets, visibilité, invités et règle d'accès`
> **Phase** : multi-utilisateur
> **Fichiers** : `src/AgentYattus.Api/Modules/Projects/` (`ProjectsModule.cs`, migration), `Modules/Identity/IdentityModule.cs`, `Contracts/Projects.cs`, `Program.cs`, tests (`ProjectsTests.cs`, `IdentityTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

| Route | Qui | Effet |
|---|---|---|
| `GET /projects` | tout le monde | Les projets que je peux lire, avec mon accès |
| `POST /projects` | tout le monde (opérateur ou admin pour une organisation) | Créer un projet personnel ou d'organisation |
| `PUT /projects/{id}` | propriétaire, admin de l'organisation | Renommer, changer la visibilité |
| `PUT` et `DELETE /projects/{id}/members/{userId}` | propriétaire, admin de l'organisation | Inviter (Éditeur ou Lecteur), retirer |
| `PUT /projects/{id}/workspace` | admin de l'instance | Choisir le dossier de travail |

## 2. La règle d'accès, en une fonction

```csharp
public static ProjectRole? Level(Project project, IReadOnlyCollection<ProjectMember> members, string userId, OrganizationRole? orgRole, bool instanceAdmin)
{
    if (project.OwnerId == userId) return ProjectRole.Owner;
    if (project.OrganizationId is not null && orgRole == OrganizationRole.Admin) return ProjectRole.Editor;
    if (project.Visibility != ProjectVisibility.Private && members.FirstOrDefault(m => m.UserId == userId) is { } member) return member.Role;
    if (project.Visibility == ProjectVisibility.Organization && project.OrganizationId is not null && orgRole is not null) return ProjectRole.Reader;
    return instanceAdmin ? ProjectRole.Reader : null;
}
```

Toute la politique de l'ADR 0009 tient en cinq lignes, **dans l'ordre** : la première qui s'applique gagne. Une fonction pure se teste en tableau : un `[Theory]` avec une ligne par cas (propriétaire, admin d'organisation, invité, projet privé…).

## 3. La même règle, en SQL

Pour lister les projets lisibles, on ne charge pas tous les projets pour les filtrer en mémoire : on traduit la règle en **requête** :

```csharp
db.Projects.Where(p =>
    p.OwnerId == userId
    || (p.OrganizationId != null && adminOf.Contains(p.OrganizationId.Value))
    || (p.Visibility != ProjectVisibility.Private && db.Members.Any(m => m.ProjectId == p.Id && m.UserId == userId))
    || (p.Visibility == ProjectVisibility.Organization && p.OrganizationId != null && myOrgs.Contains(p.OrganizationId.Value)));
```

⚠️ La règle existe maintenant **deux fois** (C# et SQL). C'est le prix de l'efficacité. Le risque : qu'elles divergent un jour. Les deux sont dans le même fichier, l'une sous l'autre, et les tests de l'API passent par les deux (`GET /projects` utilise la requête, `GET /projects/{id}` la fonction).

## 4. Un comportement qui évite une surprise

Inviter quelqu'un dans un projet **privé** le fait passer en **partagé** : sinon l'invitation ne servirait à rien, et le propriétaire ne comprendrait pas pourquoi l'invité ne voit rien. Inversement, repasser en privé coupe l'accès des invités sans les effacer : on peut rouvrir sans tout réinviter.

## 5. Le dossier de travail, vérifié

```csharp
if (path is not null && (path.Length > 500 || !Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
    return Invalid("path", "Le chemin doit être absolu et désigner un dossier existant sur le serveur.");
```

Réservé aux admins de l'instance, absolu, existant. C'est ensuite la barrière de `WorkspaceTools` (leçon 5) qui empêche les agents d'en sortir.

## 6. Deux index sur la même colonne

```csharp
e.HasIndex(p => p.OwnerId, "IX_Projects_OwnerId");
e.HasIndex(p => p.OwnerId, "IX_Projects_PersonalDefault").IsUnique().HasFilter("\"IsPersonalDefault\"");
```

Le second est un **index partiel unique** : un seul projet personnel par utilisateur, garanti par la base, même si deux requêtes arrivent en même temps. Piège rencontré : sans **nom**, EF fusionne les deux index en un seul (le partiel), et la recherche par propriétaire ne pourrait plus s'en servir. La migration générée l'a montré.

## 7. Correction de sécurité : la recherche de personnes

La revue de sécurité automatique a signalé que `GET /users/lookup` permettait de parcourir tout l'annuaire (« aa », « ab »…). La recherche partielle ne porte maintenant que sur les **collègues** (une organisation en commun). Les autres ne répondent qu'à leur **nom exact** : on peut partager avec quelqu'un dont on connaît le nom, pas découvrir qui utilise l'instance.

## 8. Quiz

1. Pourquoi l'ordre des lignes de `Level` compte-t-il ?
2. Pourquoi la règle existe-t-elle aussi en SQL, et quel est le risque ?
3. Que garantit l'index partiel unique ?
4. Pourquoi la recherche partielle est-elle limitée aux collègues ?

<details>
<summary>Réponses du quiz</summary>

1. La première règle qui s'applique gagne : un propriétaire reste propriétaire même s'il est aussi invité.
2. Pour filtrer dans la base plutôt qu'en mémoire ; le risque est que les deux versions divergent.
3. Un seul projet personnel par utilisateur, même avec deux requêtes simultanées.
4. Pour qu'on ne puisse pas énumérer tout l'annuaire en essayant toutes les paires de lettres.

</details>
