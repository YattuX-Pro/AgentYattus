# Leçon 51 — Mesurer ce que coûte une mission, et le régler sans redémarrer

> **Commit** : `docs(adr): coût des missions, budget et réglages de l'instance`
> **Phase** : compléter l'admin
> **Fichiers** : `docs/adr/0010-cout-budget-reglages.md`

## 1. Ce qu'on décide

- Chaque mission garde ses **tokens** et son **coût estimé**.
- Les **réglages** (tours, durée, missions simultanées, fichiers exclus, rétention, budget) passent de `appsettings.json` à la **base**, modifiables dans l'admin, sans redémarrage.
- Au **plafond** du budget, les missions payantes s'arrêtent ; les gratuites continuent.

> **Une image** : un compteur d'électricité. On ne lit pas la facture du fournisseur à chaque minute : on lit le compteur (les tokens), on multiplie par le tarif affiché (le catalogue), et on coupe les appareils gourmands quand on atteint le budget du mois, sans couper la lumière gratuite.

## 2. Estimation ou facture ?

Le coût calculé est une **estimation** : tokens × prix du catalogue. Le fournisseur peut facturer autrement (cache de prompt, remises). L'ADR le dit noir sur blanc, pour que personne ne prenne le chiffre de l'admin pour un relevé bancaire.

## 3. Pourquoi des micro-dollars ?

```
0,000 123 $  →  123 micro-dollars (un entier)
```

Additionner et comparer des nombres décimaux n'est pas pris en charge partout (SQLite, utilisé par les tests). Un **entier** l'est partout, et ne perd aucune précision. On convertit en dollars seulement à l'affichage.

C'est la même idée que ranger un prix en **centimes** dans une application de paiement.

## 4. Une configuration qui vit

| Avant | Après |
|---|---|
| `appsettings.json`, lu au démarrage | Une ligne en base, lue à chaque mission |
| Changer = redémarrer l'API | Changer = cliquer sur Enregistrer |
| Visible seulement de qui a accès au serveur | Visible et modifiable par les administrateurs de l'instance |

`appsettings.json` garde un rôle : donner les **valeurs de départ**, la première fois.

## 5. Quiz

1. Pourquoi le coût affiché n'est-il qu'une estimation ?
2. Pourquoi ranger le coût en micro-dollars ?
3. Que se passe-t-il au plafond du budget pour une mission sur un modèle gratuit ?

<details>
<summary>Réponses du quiz</summary>

1. Il vient des tokens et du prix du catalogue ; le fournisseur peut appliquer des remises ou un cache.
2. Un entier s'additionne et se compare partout, sans perte de précision.
3. Elle passe : le blocage ne vise que les modèles payants.

</details>
