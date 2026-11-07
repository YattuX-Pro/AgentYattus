# Leçon 80 — Un menu latéral vivant : hauteur animée, rail et indicateur

> **Commit** : `feat(admin): menu latéral refait et animé`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/app/shell/` (`shell.html`, `shell.ts`), `styles.css` (classes `ay-…`)

## 1. Ce qui ne marchait pas

L'élément actif ressemblait au survol (même gris), les couleurs changeaient d'un coup, les groupes s'ouvraient sans animation, et le menu blanc se fondait dans une page presque blanche.

## 2. Ce qu'on construit

- Un **panneau flottant** (bord arrondi, ombre douce), le logo dans une tuile éclairée.
- L'élément actif : une **pastille** vert-de-gris clair et une **barre** qui se déplie (`scaleY` de 0 à 1).
- Au survol, l'icône glisse de 2 px ; à l'ouverture d'un groupe, les liens entrent l'un après l'autre (30 ms d'écart).
- Un mode **rail** (icônes seules, 76 px), retenu d'une visite à l'autre ; le contenu suit en glissant.

## 3. Animer une hauteur inconnue

`height: auto` ne s'anime pas. L'astuce : une grille dont la seule ligne passe de `0fr` à `1fr`.

```html
<div class="grid transition-[grid-template-rows]" [style.grid-template-rows]="ouvert ? '1fr' : '0fr'">
  <div class="min-h-0 overflow-hidden">…les liens…</div>
</div>
```

La grille anime la fraction ; l'enfant, à `min-height: 0` et `overflow: hidden`, se plie avec elle. Attention : un `padding` sur l'enfant reste visible quand c'est plié ; on le met à l'extérieur.

## 4. Quiz

1. Pourquoi l'élément actif a-t-il une couleur différente du survol ?
2. Comment anime-t-on l'ouverture d'un groupe dont on ne connaît pas la hauteur ?

<details>
<summary>Réponses du quiz</summary>

1. Pour qu'on sache où l'on est d'un coup d'œil, même quand la souris survole un autre lien.
2. Avec une grille dont la ligne passe de 0fr à 1fr, et un enfant à min-height 0 qui masque ce qui dépasse.

</details>
