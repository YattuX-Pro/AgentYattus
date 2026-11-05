# Leçon 28 — Évaluer : le coût par mission réussie, par modèle

> **Commit** : `test(eval): coût par mission réussie, par modèle`
> **Phase** : 4, MVP
> **Fichiers** : `evals/` (`cases.json`, `workspaces/orders-api/`, `AgentYattus.Evals/` : `Pricing.cs`, `EvalCase.cs`, `ModelEval.cs`, `ScoringTests.cs`, `HarnessTests.cs`)

## 1. Ce qu'on construit

Un **banc d'essai** qui répond à une question très concrète : **quel modèle utiliser ?**

```
| Modèle            | Cas                       | Réussi | Coût      |
| claude-haiku-5-5  | question-appel-bloquant   | oui    | 0,0021 $  |
| claude-haiku-5-5  | revue-performance         | non    | 0,0090 $  |
| claude-sonnet-5-5 | revue-performance         | oui    | 0,1400 $  |
| ...               | par mission réussie : …   |        |           |
```

(Chiffres illustratifs : l'évaluation réelle n'a pas encore été lancée.)

## 2. Le problème : le prix au token est trompeur

Haiku 5.5 coûte **20 fois moins** que Sonnet 5.5 au token. Mais si Haiku échoue une mission sur deux, il faut la relancer, et une mission ratée ne vaut rien. La bonne mesure est donc :

> **coût par mission réussie** = coût total ÷ nombre de missions réussies

C'est ce qu'on appelle une **évaluation** (*eval*) : un jeu de cas **fixe**, rejoué à l'identique sur chaque modèle (ou après chaque changement de prompt), avec une notation **automatique**. Sans évaluation, on choisit un modèle ou on modifie un prompt « au feeling ».

## 3. Les pièces

### 3.1 Un workspace avec des défauts plantés — `evals/workspaces/orders-api/`

Trois petits fichiers C# qui contiennent **exprès** trois défauts : un `.Result` bloquant, une requête N+1, un `CancellationToken` ignoré. On sait donc **à l'avance** ce qu'un bon agent doit trouver. C'est une **vérité terrain**.

### 3.2 Des cas — `evals/cases.json`

```json
{
  "name": "revue-performance",
  "goal": "Fais la revue de performance de ce projet.",
  "kind": "Review",
  "mustMention": [".Result", "N+1", "CancellationToken"],
  "reportMustMention": [".Result", "OrderRepository.cs"]
}
```

Chaque cas donne l'objectif, le type de mission, et **les preuves attendues**. Les cas sont dans un fichier de données : en ajouter un ne demande aucun code.

### 3.3 La notation — `EvalCase.Grade`

```csharp
if (status != MissionStatus.Completed) return new(false, $"mission {status}");
var missing = MustMention.Where(word => result?.Contains(word, StringComparison.OrdinalIgnoreCase) != true).ToList();
```

Réussi = mission terminée + tous les mots-clés cités + (pour une revue) un `REPORT.md` qui contient ce qu'il doit contenir. La raison d'un échec est affichée (« réponse sans : .Result »).

```csharp
// ponytail: notation par mots-clés, simple et gratuite ; un juge LLM quand il faudra noter la qualité de rédaction.
```

**Limite assumée** : les mots-clés vérifient que les bons défauts sont **trouvés**, pas que le rapport est **bien écrit**. Pour juger la rédaction, on utilise un **juge LLM** : un autre modèle qui note selon une grille. C'est plus riche, mais payant, et lui-même à valider.

### 3.4 Le coût — `Pricing.Cost`

```csharp
["claude-haiku-5-5"] = (0.10m, 0.50m),   // $ par million de tokens, entrée / sortie
["claude-sonnet-5-5"] = (2m, 10m),
["claude-opus-5-5"] = (4m, 20m),
```

Les prix publics, relevés le 5 novembre 2026. Un piège, couvert par un test : **Haiku 5.5 coûte plus cher au-delà de 100 000 tokens d'entrée dans une même requête** (0,50 $ / 2,50 $). Le calcul se fait donc **par appel**, pas sur le total.

`decimal` et non `double` : pour de l'argent, on évite les erreurs d'arrondi binaires.

### 3.5 Les tokens viennent des traces

```csharp
ActivityStopped = activity =>
{
    if (activity.DisplayName.StartsWith("chat", StringComparison.Ordinal))
        calls.Enqueue((Convert.ToInt64(activity.GetTagItem("gen_ai.usage.input_tokens") ?? 0L), ...));
},
```

On réutilise l'**OpenTelemetry** de la leçon 21 : chaque appel au modèle émet un span avec ses tokens. L'évaluation n'a besoin d'**aucun** code dans l'agent : elle écoute ce qui existe déjà. C'est un bénéfice concret de l'observabilité.

### 3.6 Le harnais — `ModelEval`

Pour chaque modèle et chaque cas :

1. une **copie neuve** du workspace (un rapport écrit par un cas ne fausse pas le suivant) ;
2. l'**API complète** en mémoire, branchée sur le vrai modèle ;
3. la mission est lancée ; à chaque demande d'approbation, **le harnais approuve** : il joue le rôle de l'humain, pour pouvoir noter le rapport ;
4. notation, coût, ligne du tableau.

## 4. Payant, donc jamais par accident

```csharp
Assert.SkipWhen(models.Length == 0, "Évaluation payante : définir AGENTYATTUS_EVAL_MODELS pour la lancer.");
```

L'évaluation réelle est **ignorée** tant que la variable `AGENTYATTUS_EVAL_MODELS` n'est pas définie. La CI ne lance que les tests gratuits.

Ce qui est gratuit et testé à chaque build :

| Test | Vérifie |
|---|---|
| `Cost_uses_the_public_price_per_million_tokens` | Les prix de base |
| `Haiku_costs_more_beyond_100k_input_tokens_in_one_request` | Le palier de Haiku |
| `A_mission_succeeds_only_if_it_cites_everything_expected` | La notation d'une question |
| `A_review_also_needs_a_report_with_the_expected_findings` | La notation d'une revue |
| `The_cases_file_is_valid` | `cases.json` se lit |
| `A_review_is_approved_graded_on_its_report_and_priced_from_the_traces` | **Le harnais de bout en bout**, avec un faux modèle : approbation, lecture du rapport, coût calculé depuis les traces |

Le dernier test garantit que le jour où tu lances l'évaluation payante, la plomberie marche déjà.

## 5. Lancer l'évaluation (payant)

Avec ta clé dans les user-secrets de l'API (leçon 17). Ordre de grandeur, à vérifier : quelques centimes pour Haiku, quelques dizaines de centimes pour Sonnet, sur les 3 cas.

```bash
AGENTYATTUS_EVAL_MODELS=claude-haiku-5-5,claude-sonnet-5-5 dotnet test --project evals/AgentYattus.Evals --output Detailed
```

Le tableau s'affiche dans la sortie du test.

## 6. Quiz

1. Pourquoi le coût par mission réussie est-il plus juste que le prix au token ?
2. Pourquoi plante-t-on des défauts dans le workspace d'évaluation ?
3. D'où viennent les tokens comptés, et pourquoi est-ce pratique ?
4. Pourquoi le coût se calcule-t-il appel par appel pour Haiku ?
5. Quelle est la limite d'une notation par mots-clés ?

## 7. Pour aller plus loin

- Définir des critères et construire des tests : <https://docs.claude.com/en/docs/test-and-evaluate/develop-tests>
- Prix des modèles : <https://platform.claude.com/docs/en/about-claude/pricing>
- Building effective agents (évaluer avant de complexifier) : <https://www.anthropic.com/engineering/building-effective-agents>

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'une mission ratée ne vaut rien et doit être relancée : un modèle bon marché qui échoue souvent peut coûter plus cher au final.
2. Pour connaître à l'avance la bonne réponse (vérité terrain) et noter automatiquement.
3. Des traces OpenTelemetry de chaque appel au modèle ; l'évaluation n'ajoute aucun code dans l'agent.
4. Parce que le tarif de Haiku change au-delà de 100 000 tokens d'entrée dans une même requête.
5. Elle vérifie que les bons éléments sont cités, pas la qualité de la rédaction ; pour ça, il faudrait un juge LLM.

</details>
