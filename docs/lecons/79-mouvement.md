# Leçon 79 — Le mouvement partout, en une seule couche

> **Commit** : `feat(admin): transitions sur chaque clic et chaque page, placeholders partout`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/styles.css`, `app.config.ts`, `ui/` (dialog, sheet, alert-dialog, select, dropdown), pages (placeholders, petites finitions)

## 1. La règle

Chaque clic répond par une transition douce, chaque changement de page aussi, et chaque champ montre un exemple (*placeholder*). Trois agents ont audité l'admin en parallèle (le menu, les pages de pilotage, les pages de configuration) : plus de vingt endroits sans transition et une douzaine de champs vides.

## 2. Une couche globale plutôt que cent retouches

Plutôt que d'ajouter `transition-colors` à chaque bouton, une règle de base couvre tout ce qui se clique :

```css
a, button, [role='button'], … { transition-property: color, background-color, border-color, box-shadow, opacity, transform; transition-duration: 160ms; }
button:not(:disabled):active { transform: scale(.98); }
```

De même, les lignes de tableau, les éléments de liste et les cartes **entrent** en fondu (6 px de glissement), décalées de 25 ms l'une après l'autre. Une animation CSS se joue quand l'élément entre dans la page : un filtre qui recrée les lignes les fait donc rentrer à nouveau. Pour cela, une liste doit être suivie par une clé stable (`track r.at + r.missionId`), pas par sa position (`track $index`), sinon Angular réutilise les mêmes lignes et rien ne bouge.

## 3. Changer de page : les transitions de vue

```ts
provideRouter(routes, withViewTransitions({ skipInitialTransition: true }))
```

Le navigateur photographie l'ancienne page, affiche la nouvelle, et anime le passage. `view-transition-name: page` sur la zone de contenu limite l'animation à la page : le menu reste immobile.

## 4. Respecter ceux que le mouvement gêne

`@media (prefers-reduced-motion: reduce)` coupe toutes les animations. C'est un réglage du système (accessibilité), pas une option de plus à gérer.

## 5. Quiz

1. Pourquoi une règle globale plutôt qu'une classe sur chaque bouton ?
2. Pourquoi `track $index` empêche-t-il l'animation d'entrée après un filtre ?

<details>
<summary>Réponses du quiz</summary>

1. Elle couvre aussi les éléments qu'on oubliera d'écrire demain : le comportement est le même partout, sans répétition.
2. Angular garde les mêmes éléments et change leur contenu : aucun élément n'« entre », donc l'animation ne se rejoue pas.

</details>
