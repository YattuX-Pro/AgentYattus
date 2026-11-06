# Leçon 54 — Le budget mensuel : alerter, puis bloquer le payant

> **Commit** : `feat(settings): budget mensuel et blocage au plafond`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Missions/` (`MissionsModule.cs`, `MissionWorker.cs`), `Modules/Agents/AgentsModule.cs`, `Contracts/Settings.cs`, tests (`BudgetTests.cs`)

## 1. Ce qu'on construit

- `GET /budget` (admins) : le plafond, ce qui est dépensé ce mois-ci, et deux drapeaux, **alerte** (seuil dépassé) et **atteint**.
- Au plafond, si le blocage est activé, une mission dont un rôle utiliserait un modèle **payant** échoue **avant** d'appeler le modèle, avec un message et l'événement `budget:atteint`.

## 2. Vérifier avant de dépenser

```csharp
if (tuning is { BlockAtBudget: true, MonthlyBudget: { } budget }
    && await MissionsModule.SpentThisMonthMicrosAsync(db, cancellationToken) >= budget * 1_000_000m
    && await UsesPaidModelAsync(mission.Kind))
```

Le contrôle a lieu **avant** le premier appel au modèle. Une fois l'appel parti, il est facturé. Le test le prouve : le faux modèle de la seconde mission n'a pas de réponse prévue, et l'appeler aurait fait échouer le test autrement.

Un **motif de propriété** (`tuning is { BlockAtBudget: true, MonthlyBudget: { } budget }`) vérifie deux propriétés et récupère le budget en une ligne.

## 3. Le gratuit ne s'arrête pas

```csharp
var model = await llm.GetModelAsync(role, cancellationToken);
if (!model.StartsWith("gratuit", StringComparison.Ordinal) && pricing.IsPaid(model))
    return true;
```

Seuls les rôles qui travaillent sur ce type de mission comptent (`AgentsModule.RolesFor`) : une question n'utilise que l'Analyste. La chaîne gratuite et les modèles sans prix (Ollama) passent toujours. Le budget protège le portefeuille sans arrêter l'équipe.

## 4. Le mois civil, en temps universel

On compte depuis le 1er du mois à minuit **UTC**. Choisir l'UTC évite qu'un serveur en France et un autre au Canada n'aient pas le même « mois ». La limite est notée : le filtre du mois se fait en mémoire, parce que SQLite ne compare pas les dates avec fuseau.

## 5. Quiz

1. Pourquoi contrôler le budget avant le premier appel au modèle ?
2. Pourquoi une mission sur Ollama passe-t-elle au plafond ?
3. Pourquoi compter le mois en temps universel ?

<details>
<summary>Réponses du quiz</summary>

1. Un appel parti est facturé : il faut refuser avant.
2. Ollama n'a pas de prix au catalogue : il ne coûte rien, le budget ne le concerne pas.
3. Pour que le mois soit le même partout, quel que soit le fuseau du serveur.

</details>
