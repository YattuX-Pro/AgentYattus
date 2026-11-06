# Leçon 65 — Les missions en direct : SignalR dans le navigateur

> **Commit** : `feat(admin): missions en direct par SignalR`
> **Phase** : compléter l'admin
> **Fichiers** : `src/AgentYattus.Admin/` (`package.json`, `src/app/core/live.ts`, `pages/missions/`, `pages/overview/overview.ts`, `shell/`, specs)

## 1. Ce qu'on construit

- L'admin écoute le hub `/hubs/missions` (leçon 15). Chaque événement recharge la liste des missions, la vue d'ensemble, et le détail ouvert.
- Le rafraîchissement toutes les 10 secondes disparaît.
- Un voyant **En direct / Hors ligne** dans la barre du haut.

## 2. Le jeton dans l'adresse

```ts
.withUrl('/api/hubs/missions', { accessTokenFactory: () => this.auth.accessToken ?? '' })
```

Un navigateur ne peut pas ajouter d'en-tête `Authorization` à un WebSocket. Le client SignalR met donc le jeton dans l'adresse (`?access_token=…`), et l'API l'accepte **pour ce seul chemin** (leçon 30). `accessTokenFactory` est rappelé à chaque connexion : après un renouvellement (leçon 39), c'est le jeton frais qui part.

Le relais du serveur de développement transmet aussi les WebSockets (`"ws": true`, leçon 39).

## 3. Regrouper les événements

```ts
live.events.pipe(debounceTime(300), takeUntilDestroyed(inject(DestroyRef))).subscribe(() => { ... });
```

Une mission émet plusieurs événements en rafale (En cours, puis Terminée). `debounceTime(300)` attend 300 ms de calme avant de recharger : une requête par rafale, pas une par événement. `takeUntilDestroyed` coupe l'abonnement quand on quitte la page, comme `DestroyRef` pour le minuteur de la leçon 43.

## 4. Ne pas perdre la lecture en cours

Recharger le détail ouvert ne doit pas remettre à zéro le contrôle « faites défiler jusqu'au bout » (leçon 43), sauf si la **demande d'approbation a changé** :

```ts
if (fresh.pendingApproval?.id !== this.selected()?.pendingApproval?.id) this.readAll.set(false);
```

## 5. Le direct suit les droits

Le serveur n'envoie à chacun que les événements des missions qu'il peut lire (leçon 48). L'admin n'a rien à filtrer : il reçoit ce qu'il a le droit de voir.

## 6. Quiz

1. Pourquoi le jeton part-il dans l'adresse ?
2. Que fait `debounceTime(300)` ici ?
3. Quand le contrôle « tout lu » est-il remis à zéro lors d'un rechargement ?

<details>
<summary>Réponses du quiz</summary>

1. Un navigateur ne peut pas mettre d'en-tête sur un WebSocket ; l'API ne l'accepte dans l'adresse que pour le hub.
2. Elle attend 300 ms sans événement avant de recharger : une requête par rafale.
3. Seulement si la demande d'approbation a changé : on n'approuve pas un contenu qu'on n'a pas lu.

</details>
