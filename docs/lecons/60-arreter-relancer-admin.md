# Leçon 60 — Arrêter et relancer depuis l'admin

> **Commit** : `feat(admin): arrêter et relancer depuis l'admin`
> **Phase** : compléter l'admin
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`core/api.ts`, `core/events.ts`, `pages/missions/` : `missions.ts`, `missions.html`, `filter.ts`)

## 1. Ce qu'on construit

Dans le panneau d'une mission :

- **Arrêter** (en file, en cours, en pause), après une confirmation ;
- **Relancer** (échouée ou arrêtée), qui ouvre aussitôt la nouvelle mission ;
- « relance de 01a1b1… » dans l'en-tête d'une relance, un filtre **Arrêtées**, et l'événement « Arrêt demandé » dans le déroulé.

## 2. Confirmer ce qui ne se défait pas

```html
<button hlmAlertDialogCancel>Continuer la mission</button>
<button hlmAlertDialogAction variant="destructive" (click)="stop()">Arrêter</button>
```

Arrêter abandonne le travail en cours et l'écriture en attente : on ne peut pas reprendre. La confirmation dit **ce qui se passe** (l'agent s'arrête à sa prochaine étape, le journal reste, on peut relancer), et le bouton de refus dit ce qu'il garde (« Continuer la mission ») plutôt qu'un vague « Annuler », ambigu quand l'action elle-même est une annulation.

Relancer, en revanche, ne détruit rien (une nouvelle mission) : pas de confirmation.

## 3. Afficher ce que le serveur permet

```ts
protected readonly canStop = (m: Mission) => m.status === 'Pending' || m.status === 'Running' || m.status === 'AwaitingApproval';
protected readonly canRetry = (m: Mission) => (m.status === 'Failed' || m.status === 'Cancelled') && !!m.projectId;
```

Les boutons suivent les règles du serveur (leçons 58 et 59). Si l'état a changé entre-temps (la mission vient de finir), le serveur répond 409, et l'écran l'affiche.

## 4. Quiz

1. Pourquoi Arrêter demande-t-il une confirmation, et pas Relancer ?
2. Pourquoi le bouton de refus s'appelle-t-il « Continuer la mission » ?

<details>
<summary>Réponses du quiz</summary>

1. Arrêter abandonne un travail qu'on ne peut pas reprendre ; Relancer crée une nouvelle mission sans rien détruire.
2. « Annuler » serait ambigu quand l'action proposée est elle-même un arrêt : on nomme ce que le bouton garde.

</details>
