# Leçon 49 — La page Projets : partager, inviter, choisir un dossier

> **Commit** : `feat(admin): page Projets (visibilité, invités, dossier de travail)`
> **Phase** : multi-utilisateur
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`core/api.ts`, `shared/user-picker.ts`, `pages/projects/` : `projects.ts`, `projects.html`, `logic.ts`, `logic.spec.ts`, `pages/missions/`, `shell/`, `app.routes.ts`)

## 1. Ce qu'on construit

- La liste des projets, filtrée par **espace** (Personnels, puis chaque organisation) et par texte.
- Un panneau de détail : **visibilité** (Privé, Partagé, Organisation), **invités** (Éditeur ou Lecteur), **dossier de travail**, et un lien vers les missions du projet.
- **Nouveau projet** : nom, espace, visibilité.
- Dans Missions, les colonnes **Projet** et **Auteur**, et le filtre `?projet=`.

## 2. L'écran suit les droits, le serveur les impose

```html
<hlm-toggle-group ... [disabled]="!p.canManage || busy()">
```

L'API renvoie `canManage` et `myAccess` pour chaque projet. L'écran **désactive** ce que l'on ne peut pas faire, et dit pourquoi (« Seuls le propriétaire et les administrateurs de l'organisation la changent »). Mais c'est l'API qui refuse (leçon 47) : l'écran évite seulement une erreur à l'utilisateur.

Un administrateur de l'instance voit tous les projets, en **Lecteur** quand il n'en est pas membre (ADR 0009) : il peut choisir le dossier de travail, mais pas changer le partage du projet privé de quelqu'un.

## 3. Choisir parmi des options : le groupe de bascules

```html
<hlm-toggle-group type="single" [value]="p.visibility" (valueChange)="setVisibility(p, $event)">
  <button hlmToggleGroupItem value="Private">Privé</button>
  ...
</hlm-toggle-group>
```

Pour 2 à 7 choix visibles d'un coup, un **groupe de bascules** vaut mieux qu'une liste déroulante : on voit toutes les options sans cliquer. La règle vient de spartan (`rules/forms.md`). « Organisation » est désactivé pour un projet personnel : on ne propose pas une option que le serveur refuserait.

## 4. Chercher quelqu'un sans inonder le serveur

```ts
clearTimeout(this.timer);
this.timer = setTimeout(async () => this.results.set(await this.api.lookup(value)), 250);
```

C'est un **anti-rebond** (*debounce*) : on attend que la frappe s'arrête 250 ms avant d'interroger l'API. Pour taper « diallo », cela fait une requête au lieu de six. Le composant `app-user-picker` servira aussi à la page Organisations.

Si personne n'est trouvé, l'écran explique pourquoi : le compte doit exister dans Keycloak **et** s'être connecté une fois (l'annuaire se remplit à la connexion, leçon 46).

## 5. Un signal qui peut être vide

```ts
protected readonly selected = computed<Project | null>(() => ... ?? this.shown().at(0) ?? null);
```

`tableau[0]` est typé `Project`, même si le tableau est vide : TypeScript ne le signale pas. `at(0)` est typé `Project | undefined`, ce qui est juste. Le compilateur Angular l'a remarqué : il signalait un `?.` inutile, preuve que le type mentait.

## 6. Quiz

1. Pourquoi l'écran désactive-t-il des boutons, si l'API refuse de toute façon ?
2. Quand préférer un groupe de bascules à une liste déroulante ?
3. Qu'apporte l'anti-rebond à la recherche de personnes ?
4. Pourquoi `at(0)` plutôt que `[0]` ?

<details>
<summary>Réponses du quiz</summary>

1. Pour éviter à l'utilisateur une action vouée à l'échec, et lui dire pourquoi ; la sécurité reste côté API.
2. Pour peu d'options (2 à 7) qu'on gagne à voir toutes d'un coup.
3. Une requête quand la frappe s'arrête, au lieu d'une par lettre.
4. Son type inclut `undefined`, ce qui est vrai pour un tableau vide.

</details>
