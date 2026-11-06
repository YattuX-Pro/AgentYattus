# Leçon 42 — La page Agents : réutiliser un composant, regrouper des données

> **Commit** : `feat(admin): page Agents (modèle par rôle, outils et garde-fous)`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/src/app/pages/agents/` (`agents.ts`, `agents.html`, `agents.spec.ts`), `app.routes.ts`

## 1. Ce qu'on construit

Une fiche par rôle (Analyste, Rédacteur), avec :

- son **modèle** et d'où il vient : « Choisi pour ce rôle », « Suit le mode Gratuit » ou « Suit le modèle par défaut » ;
- **Changer** (le sélecteur de la leçon 41) et **Par défaut** (retirer le modèle propre) ;
- ses **outils**, en laiton quand un appel attend une **approbation humaine** ;
- un tableau des outils : qui s'en sert, et quel garde-fou s'applique.

Toutes ces données viennent de `GET /agents` et `PUT /agents/{rôle}/model` (leçon 35).

## 2. Réutiliser le sélecteur

```html
<app-model-picker
  [(open)]="pickerOpen"
  [models]="models()"
  [title]="'Modèle de ' + (editing()?.name ?? '')"
  (chosen)="set(editing()!, $event)"
/>
```

Le sélecteur ne sait rien des agents : il reçoit des modèles et rend celui qu'on a choisi. Chaque page décide quoi en faire. C'est le gain d'un composant **sans dépendance à son contexte**.

## 3. Regrouper sans boucle imbriquée dans le gabarit

```ts
export function toolRows(agents: Agent[]) {
  const rows = new Map<string, { ...; agents: string[] }>();
  for (const agent of agents)
    for (const tool of agent.tools) {
      const row = rows.get(tool.name) ?? { ...tool, agents: [] };
      row.agents.push(agent.name);
      rows.set(tool.name, row);
    }
  return [...rows.values()];
}
```

L'API donne « des agents avec leurs outils » ; le tableau veut « des outils avec leurs agents ». Une `Map` retourne la relation en un passage. On calcule cela dans un `computed`, pas dans le gabarit : le gabarit affiche, il ne calcule pas.

## 4. Montrer d'où vient une valeur

« Suit le mode Gratuit » ou « Choisi pour ce rôle » : l'administrateur voit **pourquoi** l'agent utilise ce modèle, pas seulement lequel. C'est l'ordre de préférence de la leçon 36 (modèle du rôle, puis chaîne gratuite, puis défaut), rendu visible.

## 5. Quiz

1. Pourquoi le sélecteur de modèle ne connaît-il pas les agents ?
2. Pourquoi `toolRows` est-elle une fonction à part plutôt qu'une double boucle `@for` dans le gabarit ?
3. Que se passe-t-il quand on clique sur « Par défaut » ?

<details>
<summary>Réponses du quiz</summary>

1. Pour être réutilisable : chaque page décide quoi faire du modèle choisi.
2. Pour la tester seule et garder le gabarit simple : il affiche, il ne calcule pas.
3. L'écran envoie `PUT /agents/{rôle}/model` avec `null` : le rôle revient à la chaîne gratuite ou au modèle par défaut.

</details>
