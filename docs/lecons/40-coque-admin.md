# Leçon 40 — La coque : menu latéral déroulant, fil d'Ariane, thème

> **Commit** : `feat(admin): coque avec menu latéral déroulant, recherche et thème`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`shell/shell.ts`, `shell.html`, `nav.ts`, `nav.spec.ts`, `page-header.ts`, `pages/soon.ts`, `app.routes.ts`, `ui/badge/…/hlm-badge.ts`)

## 1. Ce qu'on construit

La **coque** (*shell*) qui entoure toutes les pages :

- un **menu latéral** en groupes déroulants (Pilotage, Configuration), qui retient ce qu'on a ouvert ;
- une **barre du haut** : fil d'Ariane, recherche (⌘K) et choix du thème ;
- sur un écran étroit, le menu se cache derrière un bouton ☰.

Les groupes Administration (Organisation, Utilisateurs, Réglages) viendront avec la phase multi-utilisateur : on n'affiche pas de lien vers une page qui n'existe pas.

## 2. Des routes imbriquées

```ts
{
  path: '',
  component: Shell,
  canActivate: [adminGuard],
  children: [
    { path: 'missions', component: Missions },
    ...
  ],
}
```

La coque est une page dont le `<router-outlet />` affiche la page **enfant**. La garde est posée **une fois**, sur le parent : chaque enfant en hérite.

## 3. Les groupes déroulants avec spartan

```html
<div hlmCollapsible [expanded]="open()[group.label]" (expandedChange)="setGroup(group.label, $event)">
  <button hlmCollapsibleTrigger>{{ group.label }}</button>
  <div hlmCollapsibleContent>…liens…</div>
</div>
```

`hlmCollapsible` vient de Brain : le bouton reçoit `aria-expanded` et `aria-controls`, et le clavier fonctionne. On n'écrit que la partie propre à Agent Yattus, l'état ouvert ou fermé retenu dans `localStorage`.

## 4. Le menu décrit par des données

```ts
export const NAV: NavGroup[] = [
  { label: 'Pilotage', items: [{ path: '/missions', label: 'Missions', icon: 'lucideActivity' }, …] },
  …
];
```

Le menu **et** le fil d'Ariane lisent la même liste. Ajouter une page, c'est ajouter une ligne. `locate()` retrouve le groupe et le titre d'une adresse, et un test le vérifie.

## 5. Suivre l'adresse avec un signal

```ts
private readonly url = toSignal(
  this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map((e) => e.urlAfterRedirects)),
  { initialValue: this.router.url },
);
protected readonly here = computed(() => locate(this.url()));
```

`toSignal` transforme un flux RxJS (les événements du routeur) en **signal**. `computed` recalcule le fil d'Ariane seulement quand l'adresse change.

## 6. Adapter un composant copié

spartan a copié le badge chez nous : on lui ajoute nos deux états, sans rien forker.

```ts
success: 'bg-primary-soft text-primary',   // exécuté
warning: 'bg-warning-soft text-warning',   // décision humaine attendue
```

## 7. Le raccourci ⌘K

```ts
host: { '(document:keydown)': 'onKey($event)' },
```

Le composant écoute le clavier sur tout le document. ⌘K (ou Ctrl+K) place le curseur dans la recherche, et Entrée ouvre les missions filtrées (`/missions?q=…`).

## 8. Quiz

1. Pourquoi poser la garde sur la route parente plutôt que sur chaque page ?
2. Qu'apporte `hlmCollapsible` par rapport à un simple `@if` ?
3. Pourquoi le menu et le fil d'Ariane lisent-ils la même liste ?
4. Que fait `toSignal` ?

## 9. Pour aller plus loin

- Les routes imbriquées : <https://angular.dev/guide/routing/common-router-tasks#nesting-routes>
- spartan, Collapsible : <https://www.spartan.ng/components/collapsible>
- `toSignal` : <https://angular.dev/ecosystem/rxjs-interop>

<details>
<summary>Réponses du quiz</summary>

1. Une seule déclaration protège toutes les pages, y compris celles ajoutées plus tard.
2. L'accessibilité : `aria-expanded`, `aria-controls` et le clavier, déjà testés par spartan.
3. Pour qu'ils ne divergent jamais : une seule source de vérité.
4. Il transforme un Observable en signal, lisible dans un `computed` ou un gabarit.

</details>
