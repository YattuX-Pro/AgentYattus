# Leçon 112 — Chacun choisit le dossier de ses projets

> **Commit** : `feat(projects): chacun choisit le dossier de ses projets, sous les dossiers autorisés`
> **Phase** : A
> **Fichiers** : `Contracts/Projects.cs`, `Api/Modules/Projects/ProjectsModule.cs`, `tests/{ApiFactory,ProjectsTests}.cs`

## 1. Ce qui change

| Qui | Avant | Maintenant |
|---|---|---|
| Administrateur de l'instance | N'importe quel dossier | Pareil |
| Mode local (tu es administratrice de ta machine) | N'importe quel dossier | Pareil |
| Propriétaire, ou administrateur de l'organisation du projet | Rien | Un dossier **sous** un des `Projects:AllowedRoots` |
| Lecteur | Rien | 403 ; sans accès au projet, 404 |

`GET /projects/workspace-roots` dit au logiciel où l'on peut choisir : `Anywhere`, ou la liste des racines.

## 2. Le piège du lien symbolique

« Sous le dossier autorisé » ne suffit pas. Imagine `/srv/projets/raccourci` qui pointe vers `/etc` : le chemin commence bien par `/srv/projets/`, mais les agents liraient `/etc`. `InsideAllowedRoot` parcourt donc chaque élément du chemin sous la racine, et refuse s'il en trouve un qui est un lien. Pour la même raison, `WorkspaceTools` refuse déjà les liens à l'intérieur du dossier (leçon 05).

La racine elle-même vient de l'administrateur. On lui fait confiance, même si c'est un lien.

## 2 bis. Le dossier d'un autre (correctif après revue)

Une revue de sécurité automatique a relevé un cas : sous la même racine, Alice pouvait choisir **le dossier du projet de Bob**, et ses agents liraient les fichiers de Bob. Pour un non-administrateur, on refuse donc maintenant :

- **la racine elle-même**, qui contient les dossiers de tous ;
- **le dossier d'un autre projet**, un dossier qui le contient, ou un dossier qu'il contient (`Overlaps`).

## 3. Vérifie toi-même

`dotnet test --filter Owners_choose_a_workspace` :

- Alice choisit `autorise/mon-depot` ;
- un dossier hors des racines, ou `autorise/raccourci` (lien vers dehors), donne 400 ;
- Bob, sans accès, reçoit 404 ;
- la racine elle-même, le dossier d'Alice ou un de ses sous-dossiers, choisis par Bob pour son projet : 400.

## 4. Quiz

1. Pourquoi comparer avec `racine + séparateur` et pas seulement `StartsWith(racine)` ?

<details>
<summary>Réponse</summary>

1. Sinon `/srv/projets-secrets` passerait pour un dossier de `/srv/projets`.

</details>
