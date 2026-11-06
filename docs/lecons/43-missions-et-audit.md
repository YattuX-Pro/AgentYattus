# Leçon 43 — Missions et Audit : surveiller, décider, et ne rien approuver sans l'avoir vu

> **Commit** : `feat(admin): pages Missions et Audit, journal de toutes les missions`
> **Phase** : espace admin web
> **Fichiers** : API (`Modules/Audit/AuditModule.cs`, `Program.cs`, `Contracts/Missions.cs`, `tests/…/AuditFeedTests.cs`), admin (`core/events.ts`, `events.spec.ts`, `pages/missions/` : `missions.ts`, `missions.html`, `filter.ts`, `filter.spec.ts`, `pages/audit/audit.ts`, `app.routes.ts`)

## 1. Ce qu'on construit

- **Missions** : filtres par état avec leurs compteurs, recherche (aussi depuis ⌘K), et un **panneau latéral** par mission. Le panneau montre le déroulé (modèle, outils, bascules), le contenu à approuver et le résultat.
- **Approuver ou refuser** une écriture depuis le web, avec les mêmes garanties que le bureau.
- **Audit** : le journal de **toutes** les missions, filtrable par type d'événement. C'est une nouvelle route d'API.

## 2. Une route pour le journal global

```csharp
app.MapGet(AuditProtocol.Path, async (AuditDb db, CancellationToken ct, int limit = 200) =>
    ...db.Events.OrderByDescending(e => e.Id).Take(limit)...)
   .RequireAuthorization(Authentication.LlmAdminPolicy);
```

- Jusqu'ici, on lisait le journal **d'une** mission (`/missions/{id}/events`). L'admin veut tout voir.
- **Réservé aux administrateurs** : ce journal révèle ce que font toutes les missions de l'instance. Un test vérifie le 403 pour un observateur.
- **Borné** (500 au plus) : une route qui renverrait toute la table finirait par tomber le jour où elle contiendrait un million de lignes.

## 3. Traduire le journal

```ts
case 'bascule': return { kind, label: 'Quota épuisé, bascule', detail, tone: 'warning' };
```

Le serveur écrit des événements courts et stables (`outil:read_file`, `bascule:nvidia/…`). C'est l'**écran** qui les traduit. Le journal reste lisible par une machine, et on peut changer les libellés sans toucher aux données.

## 4. Ne rien approuver sans l'avoir vu

Le bureau applique trois règles (leçon 25). On les garde toutes :

1. **La décision vise une demande précise** : on envoie l'`approvalId` affiché. Si l'agent a changé sa demande entre-temps, l'API répond 409, et l'écran dit de recharger.
2. **Les caractères invisibles sont montrés** :

   ```ts
   reveal('a​b‮c')  // 'a⟦U+200B⟧b⟦U+202E⟧c'
   ```

   Un caractère de largeur nulle ou une inversion de sens (U+202E) peut cacher du texte à celui qui lit. On l'affiche sous forme de code. La règle est **la même que celle du bureau** : on teste les **catégories Unicode** (`\p{Cf}`, `\p{Cc}`…, avec le drapeau `u`), plutôt qu'une liste de caractères qu'on aurait forcément incomplète. On parcourt le texte **par point de code** (`for…of`), sinon les caractères hors du plan de base (le bloc des « étiquettes », U+E0000) passeraient en deux moitiés.
3. **« Approuver » s'active seulement en bas du contenu** :

   ```ts
   if (!el || el.scrollTop + el.clientHeight >= el.scrollHeight - 4) this.readAll.set(true);
   ```

Chaque argument s'affiche **brut** : le contenu d'un fichier garde ses retours à la ligne, au lieu d'un JSON plein de `\n`.

## 5. Un rafraîchissement simple

```ts
const timer = setInterval(() => void this.load(true), REFRESH_MS);
inject(DestroyRef).onDestroy(() => clearInterval(timer));
```

La liste se recharge toutes les 10 secondes. `DestroyRef` arrête le minuteur quand on quitte la page ; sans cela, il tournerait pour rien. Le hub SignalR (leçon 15) donnerait le direct ; on le branchera quand le besoin sera réel.

## 6. La recherche venue de la barre du haut

```ts
readonly q = input('');   // /missions?q=revue
```

Avec `withComponentInputBinding()`, le routeur passe les paramètres d'adresse aux **inputs** du composant. L'Audit s'en sert aussi : cliquer sur une mission ouvre `/missions?q=<id>`.

## 7. Quiz

1. Pourquoi le journal global est-il réservé aux administrateurs ?
2. Pourquoi le serveur écrit-il `bascule:…` plutôt qu'une phrase en français ?
3. Quelles sont les trois règles avant d'approuver ?
4. À quoi sert `DestroyRef` ici ?

<details>
<summary>Réponses du quiz</summary>

1. Il montre l'activité de toutes les missions de l'instance, pas seulement les siennes.
2. Pour garder des données stables et lisibles par une machine ; la traduction appartient à l'écran.
3. La décision vise l'identifiant de la demande affichée ; les caractères invisibles sont montrés ; il faut avoir fait défiler tout le contenu.
4. À arrêter le minuteur quand on quitte la page.

</details>
