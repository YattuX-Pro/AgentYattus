# Leçon 122 — Chercher dans le code, modifier un passage

> **Commit** : `feat(agents): search et edit_file, chercher dans le code et modifier un passage`
> **Phase** : C, « Mode développeur »
> **Fichiers** : `Api/Modules/Agents/{WorkspaceTools,AgentRoles,AgentFrameworkRunner}.cs`, `Api/Modules/Team/TeamContent.cs`, `tests/DeveloperToolsTests.cs`

## 1. Le problème

Avec `list_files` et `read_file`, trouver où une méthode est appelée oblige l'agent à ouvrir les fichiers un par un : des dizaines de tours, et beaucoup de tokens. Et pour corriger une ligne, `write_file` réécrit **tout le fichier**. L'humain doit relire 400 lignes pour en vérifier une, et l'agent peut en abîmer d'autres au passage.

## 2. Deux outils

**`search(text, path)`** rend les lignes trouvées, sous la forme `src/OrderService.cs:3: var total = order.Result;`.

- Le texte est cherché sans tenir compte de la casse. Ce n'est pas une expression régulière : une expression piégée pourrait bloquer le serveur (ReDoS).
- On saute les dépendances et les sorties de compilation (`node_modules`, `bin`, `obj`, `.git`…).
- Les règles de `read_file` restent valables : pas de lien symbolique, pas de fichier exclu, pas de gros fichier.
- Au plus 100 résultats, 5 000 fichiers parcourus, et 200 caractères par ligne.

**`edit_file(path, oldText, newText)`** remplace un passage **exact**.

- Un passage absent est refusé : « relis le fichier et recopie le passage exactement ».
- Un passage présent plusieurs fois est refusé : « ajoute des lignes autour ». On ne devine jamais lequel modifier.
- Modifier, c'est écrire : l'outil exige l'accord humain, comme `write_file`.

## 3. Vérifie toi-même

`dotnet test --filter DeveloperToolsTests` :

- la recherche ignore `node_modules`, `.env` et un lien vers un dossier extérieur ;
- `..` est refusé ;
- la modification refuse un passage absent ou ambigu.

## 4. Quiz

1. Pourquoi refuser un passage présent deux fois plutôt que de modifier le premier ?

<details>
<summary>Réponse</summary>

1. L'agent visait peut-être le second. Le refus le force à préciser, plutôt que de laisser le code deviner, et peut-être se tromper.

</details>
