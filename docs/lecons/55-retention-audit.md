# Leçon 55 — La rétention de l'audit, et un nom de fichier qui en cache un autre

> **Commit** : `feat(audit): purge selon la rétention, exclusions comparées au nom réel`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Audit/AuditModule.cs`, `Modules/Agents/WorkspaceTools.cs`, tests (`AuditRetentionTests.cs`, `WorkspaceToolsTests.cs`)

## 1. Ce qu'on construit

- Chaque jour, `AuditRetention` efface les événements plus anciens que la rétention choisie (90 jours au départ, 0 pour tout garder).
- Correction de sécurité : les fichiers exclus (leçon 53) sont comparés à leur **nom réel sur le disque**.

## 2. Une tâche quotidienne

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
do { ... } while (await timer.WaitForNextTickAsync(stoppingToken));
```

`PeriodicTimer` attend la prochaine échéance sans bloquer de thread. La boucle `do … while` fait une première purge **dès le démarrage**, puis une par jour. Une purge ratée est journalisée, pas propagée : une tâche de fond qui lève une exception arrête l'hôte entier (leçon 34).

## 3. Effacer sans comparer de dates en SQL

```csharp
await foreach (var e in _db.Events.OrderBy(e => e.Id).Select(e => new { e.Id, e.At }).AsAsyncEnumerable())
{
    if (e.At >= cutoff) break;
    last = e.Id;
}
return await _db.Events.Where(e => e.Id <= last).ExecuteDeleteAsync();
```

Les identifiants croissent avec le temps. On lit les événements du plus ancien au plus récent, **un par un** (`AsAsyncEnumerable`, sans tout charger), et on s'arrête au premier assez récent. Puis une seule requête efface tout ce qui précède. Cela marche avec PostgreSQL comme avec SQLite, qui ne compare pas les dates avec fuseau.

## 4. Un fichier, plusieurs noms

La revue de sécurité automatique a trouvé une faille dans l'exclusion de la leçon 53 : elle comparait le **texte demandé** par l'agent, pas le fichier réel. Or un même fichier peut s'écrire autrement :

| Demande | Pourquoi c'est `.env` | Où |
|---|---|---|
| `.ENV` | disque insensible à la casse | macOS, Windows |
| `clé.txt` (« e » + accent combinant) | disque insensible à la normalisation Unicode | macOS (APFS) |
| `.env::$DATA` | flux de données alternatif NTFS | Windows |
| `ENV~1` | nom court 8.3 | Windows |

La correction :

```csharp
if (raw.IndexOfAny([':', '~']) >= 0) return true;          // flux NTFS, noms courts : refusés d'office
var part = raw.Normalize(NormalizationForm.FormC);         // une seule forme Unicode
var actual = ActualName(current, part) ?? part;            // le nom tel qu'il est sur le disque
if (Matches(part) || Matches(actual)) return true;
```

**Deuxième passage de la revue.** Retrouver le « vrai nom » par équivalence de chaînes restait fragile, et relire le dossier parent pour chaque entrée listée coûtait un temps quadratique. La règle finale est plus simple et plus stricte :

- chaque élément du chemin doit être **exactement** (octet pour octet) un nom présent sur le disque, ou un nom nouveau qui ne ressemble à aucun nom existant ;
- un nom qui ne diffère d'un nom existant que par la casse ou la normalisation est **refusé** : c'est un alias ;
- les caractères invisibles (catégorie Unicode « format », comme l'espace de largeur nulle) sont refusés ;
- `ListFiles` compare directement les noms qu'il lit sur le disque, sans relire le dossier.

Conséquence assumée : un agent qui demande `README.MD` alors que le fichier s'appelle `README.md` reçoit un refus, avec le conseil d'utiliser le nom listé.

C'est une faille de **différence d'interprétation** (*parser differential*) : le filtre et le système de fichiers ne lisent pas le même nom de la même façon. La parade est de vérifier ce que le système de fichiers **résout**, pas ce que l'on **demande**.

## 5. Quiz

1. Pourquoi la purge ne doit-elle jamais laisser passer une exception ?
2. Pourquoi lire les événements un par un plutôt que de tout charger ?
3. Qu'est-ce qu'une faille de différence d'interprétation ?

<details>
<summary>Réponses du quiz</summary>

1. Une tâche de fond qui lève une exception arrête l'hôte, donc toute l'API.
2. Pour ne pas charger des millions de lignes en mémoire, et s'arrêter dès le premier événement récent.
3. Le contrôle et le système qui exécute ne comprennent pas la même entrée de la même façon : l'une passe le filtre et désigne autre chose.

</details>
