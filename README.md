# Agent Yattus

**Un poste de contrôle pour agents IA.** Tu confies une mission à une équipe d'agents, tu suis leur travail en direct, et **rien de sensible ne se passe sans ton accord**.

```
 Objectif ── Analyste ── Rédacteur ── Vous ── Rapport
             lit le code  écrit         approuves
             (lecture     REPORT.md     ou refuses
              seule)
```

- **Missions** : une question (l'Analyste répond en citant ses sources) ou une revue (l'Analyste rend ses constats, le Rédacteur écrit `REPORT.md`).
- **Humain dans la boucle** : chaque écriture attend ta décision ; tu vois exactement ce qui sera écrit.
- **Traçabilité** : chaque outil appelé, chaque décision, est inscrit dans un journal.
- **Observabilité** : durée et tokens de chaque appel au modèle (OpenTelemetry).
- **Évaluation** : coût par mission réussie, par modèle.

Projet open source (licence MIT), construit en .NET 10 comme support d'apprentissage : chaque commit est accompagné d'une leçon dans [`docs/lecons/`](docs/lecons/).

## Démarrer

Prérequis : SDK .NET 10 (voir `global.json`), Docker.

```bash
docker compose up -d
```

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ta-clé>" --project src/AgentYattus.Api
```

```bash
dotnet user-secrets set "Agents:Workspace" "<dossier à analyser>" --project src/AgentYattus.Api
```

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

```bash
dotnet run --project src/AgentYattus.Desktop
```

Tableau de bord des traces : <http://localhost:18888>.

La clé d'API ne doit **jamais** être écrite dans un fichier du dépôt.

## Tester

```bash
dotnet test --project tests/AgentYattus.Api.Tests
```

```bash
dotnet test --project tests/AgentYattus.Client.Core.Tests
```

```bash
dotnet test --project evals/AgentYattus.Evals
```

Aucun test n'appelle le vrai modèle : ils utilisent un faux modèle scripté. L'évaluation sur de vrais modèles est payante et ne se lance qu'explicitement (voir la [leçon 28](docs/lecons/28-evaluation.md)).

## Architecture

| Partie | Rôle |
|---|---|
| `src/AgentYattus.Api` | Monolithe modulaire ASP.NET Core : modules Missions, Agents, Audit ; PostgreSQL ; exécution en arrière-plan ; SignalR ; OpenTelemetry |
| `src/AgentYattus.Contracts` | Objets échangés entre l'API et les clients |
| `src/AgentYattus.Client.Core` | Client HTTP et SignalR, ViewModels partagés (sans dépendance d'interface) |
| `src/AgentYattus.Desktop` | Application Avalonia : Accueil, Mission en direct, Agents, Rapports |
| `spikes/AgentLoop` | La boucle d'agent écrite à la main, puis avec Agent Framework |
| `evals/` | Banc d'évaluation : cas, workspace à défauts plantés, coût par mission réussie |

Décisions d'architecture : [`docs/adr/`](docs/adr/). Modèle de sécurité : [`docs/securite.md`](docs/securite.md). Démo : [`docs/demo.md`](docs/demo.md). Feuille de route : [`docs/roadmap.md`](docs/roadmap.md).

## Licence

MIT, voir [`LICENSE`](LICENSE).
