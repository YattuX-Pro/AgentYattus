# Leçon 52 — Compter les tokens et estimer le coût de chaque mission

> **Commit** : `feat(missions): tokens et coût enregistrés par mission`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Llm/LlmModule.cs` (`ILlmPricing`), `Modules/Agents/` (`AgentsModule.cs`, `AgentFrameworkRunner.cs`), `Modules/Missions/` (`Mission.cs`, `MissionWorker.cs`, migration), `Contracts/Missions.cs`, tests (`AgentRunnerTests.cs`, `ReviewTests.cs`, `ApprovalTests.cs`, `ScriptedChatClient.cs`, `ApiFactory.cs`)

## 1. Ce qu'on construit

`MissionDto` gagne `InputTokens`, `OutputTokens` et `Cost`. Le journal gagne un événement par rôle : `usage:analyste:1000:200`.

## 2. D'où viennent les tokens

```csharp
var input = response.Usage?.InputTokenCount ?? 0;
var output = response.Usage?.OutputTokenCount ?? 0;
```

Chaque réponse d'un modèle dit combien de tokens elle a lus (**entrée**) et écrits (**sortie**). La boucle d'outils (`FunctionInvokingChatClient`) **additionne** les tours : un agent qui a appelé trois outils rend le total des quatre appels au modèle.

L'entrée et la sortie n'ont pas le même prix (la sortie coûte souvent cinq fois plus). On les garde donc séparées.

## 3. Le prix, au catalogue

```csharp
public long CostMicros(string model, long inputTokens, long outputTokens) =>
    Find(model) is { InputPrice: { } input, OutputPrice: { } output }
        ? (long)Math.Round(inputTokens * input + outputTokens * output, MidpointRounding.AwayFromZero)
        : 0;
```

Le catalogue donne des **dollars par million de tokens**. Multiplier des tokens par ce prix donne directement des **micro-dollars** (un millionième de dollar) : 1 000 tokens × 0,10 $ = 100 micro-dollars. Le test le vérifie avec Claude Haiku 5.5 : 1 000 × 0,10 + 200 × 0,50 = **200 micro-dollars**, soit 0,0002 $.

Un modèle **inconnu** du catalogue (serveur local, adresse personnalisée) coûte 0. C'est une estimation, pas une facture (ADR 0010).

## 4. Additionner les exécutions

Une mission peut s'exécuter plusieurs fois : une revue s'arrête pour une approbation, puis reprend. Chaque passage **ajoute** sa consommation :

```csharp
mission.InputTokens += usage.InputTokens;
mission.CostMicros += usage.CostMicros;
```

Un opérateur `+` sur `AgentUsage` rend le cumul des rôles lisible : `usage += analysisUsage;`.

## 5. Les tests qui listent le journal

Quatre tests comparaient le journal **exact** d'une mission. Ils ont échoué, puisqu'un événement `usage:` s'y ajoute. C'est voulu : un test qui fige une liste signale tout changement. On les met à jour en lisant ce qui a changé, pas en affaiblissant l'assertion.

## 6. Quiz

1. Pourquoi garder séparés les tokens d'entrée et de sortie ?
2. Pourquoi tokens × prix du catalogue donne-t-il des micro-dollars ?
3. Que coûte une mission sur Ollama ?

<details>
<summary>Réponses du quiz</summary>

1. Ils n'ont pas le même prix ; la sortie coûte en général plus cher.
2. Parce que le prix est par million de tokens : diviser par un million et compter en millionièmes de dollar s'annulent.
3. 0 : le modèle n'a pas de prix au catalogue.

</details>
