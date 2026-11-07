# Leçon 99 — L'efficacité d'un agent : réussir, pas seulement économiser

> **Commit** : `feat(agents): réussite et coût par mission de chaque agent`
> **Phase** : le bureau des agents
> **Fichiers** : `Missions/MissionsModule.cs` (`PerformanceAsync`), `Contracts/Agents.cs`, `Admin/…/pages/agents/`, tests

## 1. Ce qu'on construit

Sur chaque carte d'agent : ses **exécutions**, son **taux de réussite** (vert au-delà de 80 %, laiton au-delà de 50 %, cinabre en dessous, avec une petite barre) et le **coût moyen d'une mission**. Dessous, sa consommation en tokens.

## 2. Économe n'est pas efficace

Un agent qui consomme peu mais échoue une fois sur deux coûte plus cher qu'il n'en a l'air : il faut relancer. Les tokens disent ce qu'il dépense ; la réussite et le coût par mission disent ce qu'il rapporte.

## 3. Qui calcule quoi

Le module **Missions** sait le statut et le coût de chaque mission, et maintenant son agent (leçon 95). C'est donc lui qui calcule, sur une route à part (`GET /agents/performance`, administrateurs) : le module Agents ne dépend pas du module Missions, puisque c'est déjà l'inverse. Deux modules qui dépendent l'un de l'autre ne se testent plus séparément.

```csharp
.GroupBy(m => new { m.Kind, m.Agent, m.Status })
.Select(g => new { …, Count = g.Count(), Cost = g.Sum(m => m.CostMicros) })
```

La base regroupe (une ligne par type, agent et statut) ; le code répartit ensuite : une question compte pour son agent, une revue pour l'Analyste **et** le Rédacteur.

## 4. Une limite dite clairement

Le coût d'une revue est celui de la mission entière, compté pour chacun des deux agents : on ne sait pas le partager exactement. L'infobulle le dit. Mieux vaut un chiffre honnête qu'un chiffre précis en apparence.

## 5. Quiz

1. Pourquoi le taux de réussite compte-t-il autant que les tokens ?
2. Pourquoi le calcul est-il dans le module Missions ?

<details>
<summary>Réponses du quiz</summary>

1. Un échec coûte ses tokens et une relance : un agent économe qui échoue souvent revient cher.
2. Missions connaît le statut, le coût et l'agent de chaque mission ; et Missions dépend déjà d'Agents : l'inverse créerait une dépendance circulaire.

</details>
