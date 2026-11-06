# Leçon 53 — Les réglages de l'instance : en base, appliqués sans redémarrer

> **Commit** : `feat(settings): module Réglages de l'instance`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Settings/` (`SettingsModule.cs`, migration), `Modules/Agents/` (`WorkspaceTools.cs`, `AgentFrameworkRunner.cs`), `Modules/Missions/MissionWorker.cs`, `Contracts/Settings.cs`, `Program.cs`, tests (`SettingsTests.cs`, `WorkspaceToolsTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

`GET` et `PUT /settings`, réservés aux administrateurs de l'instance :

| Réglage | Effet | Départ |
|---|---|---|
| Tours maximum | Limite de la boucle d'outils | 10 (configuration) |
| Durée maximum | Au-delà, la mission est arrêtée et échoue | 15 min |
| Missions simultanées | Places du worker | 1 |
| Taille max d'un fichier lu | Refus au-delà | 256 Ko |
| Fichiers exclus | Invisibles pour les agents | `.env`, `*.pfx`, `*.key`, `id_rsa*`… |
| Rétention de l'audit | Purge (leçon 55) | 90 jours |
| Budget, alerte, blocage | Leçon 54 | aucun, 80 %, oui |

## 2. Une ligne, des colonnes nulles

```csharp
internal sealed class SettingsRow
{
    public int? MaxIterations { get; set; }      // null : valeur de départ
    public int? MaxDurationMinutes { get; set; }
    ...
}
```

Chaque colonne **nulle** reprend la valeur de départ. Un réglage jamais touché suit donc la configuration ; une fois choisi, il l'emporte. On lit la ligne **à chaque mission** : enregistrer dans l'admin suffit, sans redémarrage.

## 3. Des fichiers que les agents ne voient pas

```csharp
private bool IsExcluded(string fullPath) =>
    Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar)
        .Any(part => _excluded.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, part, ignoreCase: true)));
```

`FileSystemName.MatchesSimpleExpression` comprend `*` et `?`, comme un shell. On teste **chaque élément** du chemin : exclure `secrets` cache aussi tout ce qu'il contient. Un fichier exclu est absent des listes **et** refusé en lecture comme en écriture.

⚠️ Un motif est un **nom**, jamais un chemin : `../secret` est refusé à l'enregistrement. On ne laisse pas un réglage désigner l'extérieur du workspace.

Les valeurs de départ visent les secrets classiques (`.env`, clés privées, certificats). C'est une **défense en profondeur** : l'approbation humaine protège les écritures, l'exclusion protège les lectures, même si un fichier piégé demande à l'agent de lire `.env` (injection de prompt).

## 4. Plusieurs missions à la fois

```csharp
var max = await MaxConcurrentAsync(stoppingToken);
while (running.Count >= max)
{
    await Task.WhenAny(running);
    running.RemoveAll(t => t.IsCompleted);
}
running.Add(RunOneAsync(missionId, stoppingToken));
```

Le worker lançait une mission à la fois. Il garde maintenant une liste de tâches en cours, et attend qu'une place se libère (`Task.WhenAny`) avant de lancer la suivante. Le réglage est relu avant chaque départ. Chaque mission a sa propre **portée** d'injection (`CreateAsyncScope`), donc son propre `DbContext` : deux missions ne partagent jamais une connexion.

## 5. La durée maximum

```csharp
using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
limit.CancelAfter(maxDuration);
```

Un jeton d'annulation **lié** s'annule si l'hôte s'arrête **ou** si le délai expire. L'agent reçoit ce jeton ; au-delà du délai, ses appels sont annulés, et la mission échoue avec « Durée maximum dépassée ». La leçon 58 réutilisera ce jeton pour le bouton Arrêter.

## 6. Quiz

1. Pourquoi les colonnes des réglages peuvent-elles être nulles ?
2. Pourquoi tester chaque élément du chemin contre les motifs exclus ?
3. Pourquoi chaque mission a-t-elle sa propre portée d'injection ?
4. Qu'est-ce qu'un jeton d'annulation lié ?

<details>
<summary>Réponses du quiz</summary>

1. Nulle veut dire « valeur de départ » : on distingue « jamais réglé » de « réglé ».
2. Pour qu'exclure un dossier cache tout son contenu.
3. Un `DbContext` ne se partage pas entre deux tâches qui tournent en même temps.
4. Un jeton qui s'annule dès que l'un de ses parents s'annule (l'hôte), ou à l'expiration de son propre délai.

</details>
