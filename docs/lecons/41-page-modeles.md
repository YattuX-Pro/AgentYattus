# Leçon 41 — La page Modèles : un client typé, des dialogues et la clé qui ne reste pas

> **Commit** : `feat(admin): page Modèles (fournisseurs, défaut, mode Gratuit, catalogue)`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`core/api.ts`, `core/format.ts`, `pages/models/` : `models.ts`, `models.html`, `model-picker.ts`, `logic.ts`, `logic.spec.ts`, `app.routes.ts`, `shell/`), `angular.json`

## 1. Ce qu'on construit

L'écran Modèles du bureau (leçons 33 à 36), refait pour le web et branché sur la **même API** `/llm` :

- le **modèle par défaut**, avec son contexte et son prix ;
- le **mode Gratuit** : interrupteur, chaîne ordonnée (↑, Retirer), « ＋ Modèle gratuit », « Tout ajouter » ;
- les **connexions** : clé masquée (`•••• WXYZ`), révocation après confirmation ;
- le **catalogue** : recherche, filtre « Gratuits seulement », « Par défaut » en un clic.

Aucune ligne de C# n'a changé : l'API était déjà prête.

## 2. Un client typé

```ts
export interface LlmModel {
  connectionId: string; provider: string; id: string; name: string; contextWindow: number | null; ...
}

llm() { return this.get<LlmStatus>('/llm'); }
setFree(enabled: boolean, chain: LlmSelection[]) { return this.send('PUT', '/llm/free', { enabled, chain }); }
```

Les interfaces TypeScript **recopient** les contrats C#. Si l'API renomme un champ, le compilateur ne le verra pas : c'est la limite notée dans l'ADR 0008. Elle est acceptable tant que les contrats restent petits.

Les méthodes renvoient des **promesses** (`firstValueFrom`) : une requête HTTP donne une seule réponse, et `await` se lit mieux qu'un `subscribe` pour cela.

## 3. La logique hors du composant

```ts
export function fillChain(chain, models, providers): LlmSelection[] {
  // les modèles gratuits, les locaux d'abord, sans doublon, dix au plus
}
```

Les règles (« Tout ajouter », « Monter », la recherche) sont des **fonctions pures** dans `logic.ts` : elles reçoivent des données et en rendent d'autres, sans toucher à l'écran. On les teste en trois lignes, sans navigateur ni HTTP.

⚠️ L'interface n'est pas la garantie : si un modèle payant arrivait quand même dans la chaîne, **le serveur le refuserait** (leçon 36).

## 4. Des dialogues pilotés par des signals

```html
<hlm-dialog [state]="connectState()" (closed)="connectState.set('closed'); apiKey.set('')">
  <hlm-dialog-content *hlmDialogPortal="let ctx">…</hlm-dialog-content>
</hlm-dialog>
```

- `[state]` ouvre ou ferme le dialogue : c'est un signal qui décide, pas un clic caché.
- `*hlmDialogPortal` crée le contenu **seulement à l'ouverture**, dans une couche au-dessus de la page. Brain gère le focus piégé, Échap et le retour du focus au bouton d'origine.
- La révocation passe par un `hlm-alert-dialog` : une action irréversible demande une confirmation explicite.

## 5. La clé ne reste pas dans la page

```ts
} finally {
  // La clé ne reste jamais dans la page, que la connexion ait réussi ou non.
  this.apiKey.set('');
}
```

Le champ est `type="password"` et `autocomplete="off"`. Après l'envoi, réussi ou non, la clé est effacée. Fermer le dialogue l'efface aussi. Le serveur la chiffre (leçon 32) et ne renverra jamais que ses 4 derniers caractères.

## 6. Un composant partagé : le sélecteur

```html
<app-model-picker [(open)]="pickerOpen" [models]="pickerModels()" [freeOnly]="pickerFor() === 'chain'" (chosen)="choose($event)" />
```

- `model()` crée une liaison **dans les deux sens** (`[(open)]`) : le parent ouvre, le sélecteur ferme.
- `output()` émet le modèle choisi ; c'est le parent qui décide quoi en faire (défaut ou chaîne).

La page Agents réutilisera ce sélecteur.

## 7. Charger les pages à la demande

```ts
{ path: 'modeles', loadComponent: () => import('./pages/models/models').then((m) => m.Models) },
```

Chaque page devient un fichier séparé, téléchargé à la première visite. Le premier affichage ne charge que la coque.

## 8. Quiz

1. Pourquoi les règles de la chaîne sont-elles dans des fonctions pures ?
2. Que fait `*hlmDialogPortal` ?
3. À quels moments la clé est-elle effacée de la page ?
4. Quelle est la limite des interfaces TypeScript recopiées à la main ?

## 9. Pour aller plus loin

- spartan, Dialog et Alert Dialog : <https://www.spartan.ng/components/dialog>
- `model()` et les liaisons dans les deux sens : <https://angular.dev/guide/components/inputs#model-inputs>
- Le chargement à la demande : <https://angular.dev/guide/routing/define-routes#lazily-loaded-components>

<details>
<summary>Réponses du quiz</summary>

1. Pour les tester simplement, sans écran ni réseau, et les garder séparées de l'affichage.
2. Il crée le contenu du dialogue seulement à l'ouverture, dans une couche au-dessus de la page, avec le focus géré par Brain.
3. Après chaque envoi (réussi ou non), au retour à la liste des fournisseurs, et à la fermeture du dialogue.
4. Le compilateur ne voit pas qu'un champ a changé côté C# : l'erreur n'apparaît qu'à l'exécution.

</details>
