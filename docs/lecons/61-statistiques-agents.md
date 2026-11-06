# Leçon 61 — Des statistiques par agent, lues dans le journal

> **Commit** : `feat(agents): statistiques par agent`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Agents/AgentsModule.cs`, `Modules/Audit/AuditModule.cs`, `Contracts/Agents.cs`, `tests/…/AgentsTests.cs`, admin (`core/api.ts`, `pages/agents/`)

## 1. Ce qu'on construit

`GET /agents` ajoute, pour chaque rôle, ses **exécutions**, ses **tokens au total** et ses **tokens moyens**. Les fiches de la page Agents les affichent.

## 2. Le journal comme source

Depuis la leçon 52, chaque exécution d'un rôle inscrit `usage:analyste:1000:200`. Les statistiques ne demandent donc **aucune table de plus** : on lit ces événements et on les additionne.

```csharp
var parts = type.Split(':');
if (parts.Length != 4 || !long.TryParse(parts[2], out var input) || !long.TryParse(parts[3], out var output))
    continue;
```

Un événement mal formé est **ignoré**, pas fatal : le journal est une donnée qu'on lit avec prudence, même quand c'est nous qui l'écrivons. Le test vérifie un événement abîmé et un nombre illisible.

## 3. La limite, écrite

```csharp
// ponytail: lit tous les événements « usage: » ; une table de statistiques agrégées si le journal devient très gros.
```

Lire tous les événements `usage:` à chaque affichage reste rapide tant qu'il y en a quelques milliers. La rétention (leçon 55) les borne aussi. Le jour où ce ne sera plus le cas, une table agrégée prendra le relais. Le commentaire `ponytail:` dit où est le plafond et comment le lever.

## 4. Quiz

1. Pourquoi les statistiques n'ont-elles pas besoin d'une nouvelle table ?
2. Pourquoi ignorer un événement mal formé plutôt que de lever une erreur ?

<details>
<summary>Réponses du quiz</summary>

1. Le journal contient déjà un événement de consommation par exécution de rôle.
2. Une donnée abîmée ne doit pas empêcher d'afficher les autres ; on la saute.

</details>
