# Leçon 57 — Le coût à l'écran : indicateurs, colonnes et formats

> **Commit** : `feat(admin): coût et tokens dans la vue d'ensemble et les missions`
> **Phase** : compléter l'admin
> **Fichiers** : `src/AgentYattus.Admin/src/app/pages/` (`overview/overview.ts`, `overview.html`, `missions/missions.ts`, `missions.html`)

## 1. Ce qu'on construit

- Vue d'ensemble : les indicateurs **Coût du mois** (avec la barre du budget, en laiton au-delà de l'alerte) et **Tokens**.
- Missions : les colonnes **Tokens** et **Coût**, et le détail « entrée · sortie · coût » dans le panneau.

## 2. Afficher un petit montant sans mentir

```ts
export function usd(amount: number | null | undefined): string {
  if (!amount) return '0 $';
  const digits = amount < 0.01 ? 4 : 2;
  ...
}
```

Une question à Haiku coûte souvent moins d'un centime. Avec deux décimales, l'écran afficherait « 0,00 $ » : faux, et trompeur pour qui surveille sa dépense. Sous le centime, on montre quatre décimales (« 0,0002 $ »).

`tokens()` abrège de même : « 12,3k », « 1,2M ». Les deux fonctions utilisent `toLocaleString('fr-FR')` : la virgule décimale française, sans écrire le format à la main.

## 3. Une barre à l'échelle du budget

```ts
protected readonly budgetPercent = computed(() => {
  const b = this.budget();
  return b?.monthlyBudget ? Math.min(100, Math.round((b.spent / b.monthlyBudget) * 100)) : 0;
});
```

La barre est bornée à 100 % : un dépassement ne doit pas déborder du cadre. Sa couleur dit l'état : vert-de-gris en temps normal, laiton au-delà de l'alerte. Sans plafond, pas de barre, et l'indicateur dit « aucun plafond ».

## 4. Quiz

1. Pourquoi quatre décimales sous le centime ?
2. Pourquoi borner la barre à 100 % ?

<details>
<summary>Réponses du quiz</summary>

1. Pour ne pas afficher 0,00 $ quand une mission a coûté quelque chose.
2. Pour qu'un dépassement reste lisible dans le cadre ; le montant exact est écrit à côté.

</details>
