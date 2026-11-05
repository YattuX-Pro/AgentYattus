# Démo du MVP (5 minutes)

Objectif : montrer une revue de code à deux agents, de bout en bout, **approbation comprise**.

## Préparer (une fois)

1. `docker compose up -d` (PostgreSQL et le tableau de bord des traces).
2. Clé et workspace dans les user-secrets de l'API (voir le README). Pour la démo, le workspace d'évaluation fait un bon sujet :

```bash
dotnet user-secrets set "Agents:Workspace" "$PWD/evals/workspaces/orders-api" --project src/AgentYattus.Api
```

3. Lancer l'API, puis le desktop.

## Dérouler

| Étape | Action | Ce qu'on montre |
|---|---|---|
| 1 | Accueil : la pastille « Backend » est verte | Le desktop est connecté en temps réel (SignalR) |
| 2 | Clic sur la suggestion « Revue de performance » | La bascule REPORT.md s'allume : c'est une revue à deux agents |
| 3 | Lancer, puis ouvrir la carte | L'Analyste pulse : il lit le code (outils de lecture seule) |
| 4 | Attendre le relais | Le Rédacteur prend la main à partir des constats ; le nœud « Vous » se met à pulser en laiton |
| 5 | Lire la carte d'approbation | Le contenu exact de REPORT.md ; « Approuver » reste grisé tant qu'on n'a pas tout vu |
| 6 | « Tout voir », puis « Approuver » | Le fichier est écrit ; la mission passe à « Terminée » |
| 7 | Onglet Rapports | Ce qui a été exécuté (source : le journal d'audit), et la réponse |
| 8 | <http://localhost:18888>, onglet Traces | Chaque appel au modèle, sa durée et ses tokens, sous l'exécution de chaque agent |
| 9 | Variante : relancer et **refuser** | Rien n'est écrit, l'agent est prévenu, l'analyse reste disponible |

## Questions qu'on te posera

- *« Et si un fichier contient une instruction malveillante ? »* : l'Analyste ne peut rien écrire, et chaque écriture du Rédacteur passe par toi (voir `docs/securite.md`).
- *« Combien ça coûte ? »* : `evals/` mesure le coût par mission réussie, par modèle (leçon 28).
