# ADR 0010 — Coût des missions, budget et réglages de l'instance

- **Statut** : proposé
- **Date** : 2026-11-06

## Contexte

La maquette de l'admin affiche le coût du mois, un budget et des réglages (nombre de tours, durée, missions simultanées, rétention de l'audit, fichiers exclus). Aujourd'hui, les tokens ne vivent que dans les traces OpenTelemetry, et les réglages sont fixés dans `appsettings.json` : il faut redémarrer l'API pour les changer.

## Décision

1. **Les tokens viennent de la réponse de l'agent.** Chaque exécution d'un rôle rend son `Usage` (tokens d'entrée et de sortie, cumulés sur tous les tours d'outils). Le runner les additionne pour la mission, et inscrit un événement `usage:{rôle}:{entrée}:{sortie}` dans le journal (source des statistiques par agent).
2. **Le coût se calcule au prix du catalogue** (models.dev, ADR 0007), par million de tokens. Un modèle sans prix connu (serveur local, adresse personnalisée) ou gratuit coûte 0. C'est une **estimation** : la facture du fournisseur fait foi (remises, cache, arrondis).
3. **Le coût est rangé en micro-dollars** (un entier) : additionner et comparer des décimaux n'est pas possible partout (SQLite), un entier si.
4. **Les réglages vivent en base**, dans un module **Settings**, et s'appliquent sans redémarrage : tours maximum, durée maximum d'une mission, missions simultanées, taille maximum d'un fichier lu, fichiers exclus de la lecture, rétention de l'audit, budget mensuel, seuil d'alerte, blocage au plafond. `appsettings.json` ne donne plus que les valeurs de départ. Seuls les **administrateurs de l'instance** les modifient.
5. **Le budget protège sans bloquer le gratuit.** Au plafond, et si le blocage est activé, une mission dont un rôle utiliserait un modèle **payant** échoue tout de suite, avec un message et l'événement `budget:atteint`. Les modèles gratuits restent permis. Le mois est le mois civil, en temps universel.
6. **Les fichiers exclus** (par exemple `.env`, `*.pfx`, `secrets.json`) sont invisibles pour les agents : absents des listes, refusés en lecture et en écriture. La règle vit dans `WorkspaceTools`, pas dans les consignes.
7. **La rétention** efface chaque jour les événements d'audit plus anciens que le nombre de jours choisi (0 : tout garder).

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Lire le coût dans les traces OpenTelemetry | Déjà mesuré | Les traces ne sont pas une base de données ; elles peuvent être désactivées ou échantillonnées |
| **Usage de la réponse + prix du catalogue** | Simple, persistant, par mission | Estimation, pas la facture |
| API de facturation de chaque fournisseur | Le vrai montant | Une intégration par fournisseur, et pas toutes l'exposent |
| Réglages dans `appsettings.json` | Rien à coder | Redémarrage à chaque changement ; pas d'écran |
| Bloquer toute mission au plafond | Simple | Interdit aussi le gratuit, qui ne coûte rien |

## Conséquences

- Une mission qui échoue en cours de route ne compte pas les tokens déjà dépensés par le rôle interrompu (on ne les connaît qu'à la fin de son exécution).
- Le prix suit le catalogue du jour de la mission ; un changement de prix ne réécrit pas le passé.
