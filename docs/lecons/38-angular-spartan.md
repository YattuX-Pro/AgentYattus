# Leçon 38 — Le projet Angular, spartan/ui et la palette en clair et en sombre

> **Commit** : `feat(admin): projet Angular avec spartan/ui, thème clair et sombre, CI`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/` (`package.json`, `angular.json`, `components.json`, `.postcssrc.json`, `src/styles.css`, `src/index.html`, `src/app/core/theme.ts`, `src/app/ui/`), `.github/workflows/ci.yml`

## 1. Ce qu'on construit

Le squelette de l'espace admin : une application Angular 21 qui compile, avec les composants **spartan/ui**, la palette d'Agent Yattus en **clair et en sombre**, un premier test **Vitest** et un job de CI.

## 2. Créer le projet

```bash
npx @angular/cli@21.2.14 new agent-yattus-admin --directory AgentYattus.Admin --style=css --ssr=false --routing
```

- `--ssr=false` : pas de rendu côté serveur. Un espace admin derrière une connexion n'a rien à indexer.
- Angular 21 crée des **composants autonomes** (*standalone*, sans `NgModule`), fonctionne **sans zone.js** (la détection des changements suit les *signals*) et teste avec **Vitest**.

## 3. spartan/ui : deux couches

| Couche | Où | Rôle |
|---|---|---|
| **Brain** (`@spartan-ng/brain`) | `node_modules` | Le comportement et l'accessibilité : clavier, focus, attributs ARIA |
| **Helm** | `src/app/ui/…`, **copié chez nous** | Le style : classes Tailwind, variantes |

```bash
ng g @spartan-ng/cli:init --project=agent-yattus-admin --theme=stone
ng g @spartan-ng/cli:ui --name=button
```

`init` installe Brain, Tailwind et le *preset* de spartan. `ui` **copie** un composant Helm dans `src/app/ui` et ajoute un raccourci d'import dans `tsconfig.json` :

```json
"@spartan-ng/helm/button": ["./src/app/ui/button/src/index.ts"]
```

On écrit donc `import { HlmButton } from '@spartan-ng/helm/button'`, mais le code est **le nôtre** : on peut le modifier.

> **Une image** : Brain, c'est la charpente et l'électricité d'une maison, posées par des spécialistes. Helm, c'est la peinture et les meubles : livrés chez toi, tu les gardes, tu les repeins.

## 4. La palette : des jetons, pas des couleurs

Les composants Helm n'utilisent jamais une couleur brute (`bg-blue-500`). Ils utilisent des **jetons** : `bg-primary`, `text-muted-foreground`, `border-border`. Chaque jeton est une variable CSS :

```css
:root {           /* clair */
  --primary: #33796d;
  --background: #f6f7f5;
}
:root.dark {      /* sombre */
  --primary: #5e9e92;
  --background: #0e0f11;
}
```

On n'a pas touché un seul composant : remplacer les valeurs suffit à habiller toute l'application aux couleurs d'Agent Yattus.

Nos couleurs à nous (le laiton des décisions humaines) s'ajoutent de la même façon, puis on les **expose à Tailwind** :

```css
@theme inline {
  --color-warning: var(--warning);   /* active bg-warning, text-warning… */
}
```

## 5. Clair, sombre, ou comme le système

```ts
effect(() => {
  const choice = this.choice();
  const dark = choice === 'dark' || (choice === 'system' && this.systemDark());
  document.documentElement.classList.toggle('dark', dark);
});
```

- `choice` est un **signal** : quand on le change, l'`effect` se relance tout seul.
- `systemDark` suit `prefers-color-scheme` : si macOS passe en sombre le soir, la page suit.
- Le choix est retenu dans `localStorage`. Ce n'est **pas un secret**, contrairement à un jeton (leçon 37).

⚠️ **L'éclair blanc** : Angular démarre après le premier affichage. Sans précaution, la page s'affiche en clair puis bascule. Un petit script dans `index.html` pose la classe `dark` **avant** le premier affichage.

## 6. Le test et la CI

```ts
theme.choice.set('dark');
TestBed.tick();   // exécute les effects en attente
expect(document.documentElement.classList.contains('dark')).toBe(true);
```

jsdom (le faux navigateur des tests) ne connaît pas `matchMedia` : `src/test-setup.ts` le simule.

La CI gagne un job `admin` : `npm ci`, `ng build`, `ng test`. `npm ci` installe **exactement** les versions de `package-lock.json`, jamais une version plus récente.

## 7. Vérifie toi-même

```bash
cd src/AgentYattus.Admin
npm ci && npm test
```

## 8. Quiz

1. Quelle couche de spartan gère le clavier et l'accessibilité ? Laquelle est copiée dans le projet ?
2. Pourquoi les composants utilisent-ils `bg-primary` et pas une couleur précise ?
3. À quoi sert le script dans `index.html` ?
4. Pourquoi `npm ci` plutôt que `npm install` en CI ?

## 9. Pour aller plus loin

- spartan/ui, installation et thèmes : <https://www.spartan.ng/documentation/theming>
- Les signals d'Angular : <https://angular.dev/guide/signals>
- Tailwind 4, `@theme` : <https://tailwindcss.com/docs/theme>

<details>
<summary>Réponses du quiz</summary>

1. Brain gère le comportement et l'accessibilité ; Helm (le style) est copié dans `src/app/ui`.
2. Pour que le thème se change en un seul endroit : les variables CSS, différentes en clair et en sombre.
3. À poser la classe `dark` avant le premier affichage, pour éviter un éclair blanc.
4. `npm ci` installe exactement le `package-lock.json` : la CI teste ce que l'équipe a testé.

</details>
