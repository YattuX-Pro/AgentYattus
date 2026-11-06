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
- **Espace admin web** (Angular + spartan/ui) : vue d'ensemble, missions et approbations, audit, agents, modèles. Le bureau et le mobile servent à travailler ; la gestion se fait sur le web (ADR 0008).
- **Connexion et gestion du LLM** : connexion Keycloak (OIDC + PKCE) ; comme dans opencode, n'importe quel fournisseur (Anthropic, OpenAI, Gemini, Mistral, Groq, OpenRouter, Ollama en local…) et un sélecteur unique de modèles, tiré de models.dev ; clés chiffrées et révocables ; un modèle par agent ; un **mode Gratuit** qui enchaîne les modèles gratuits (Ollama, OpenRouter, NVIDIA, OpenCode Zen) et bascule tout seul quand un quota est épuisé, sans jamais passer à un modèle payant.

Projet open source (licence MIT), construit en .NET 10 comme support d'apprentissage : chaque commit est accompagné d'une leçon dans [`docs/lecons/`](docs/lecons/).

## Démarrer

Prérequis : SDK .NET 10 (voir `global.json`), Docker, Node 24 pour l'espace admin.

```bash
docker compose up -d
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

Espace admin web (Node 24) :

```bash
cd src/AgentYattus.Admin && npm ci && npm start
```

Puis <http://localhost:4200>, compte `dev` / `agent-yattus-dev`.

Dans l'application de bureau : **Se connecter** (compte de développement `dev` / `agent-yattus-dev`, voir [`deploy/keycloak/README.md`](deploy/keycloak/README.md)), puis l'icône de réglages : **Connecter un fournisseur** (une clé d'API, ou Ollama en local, sans clé), puis choisir le modèle. Les clés sont chiffrées sur le serveur et ne sont plus jamais affichées.

Tableau de bord des traces : <http://localhost:18888>. Console Keycloak : <http://localhost:8380/admin>.

La clé d'API ne doit **jamais** être écrite dans un fichier du dépôt. En développement, elle peut aussi venir des user-secrets de l'API (`Anthropic:ApiKey`).

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

```bash
cd src/AgentYattus.Admin && npm test
```

Aucun test n'appelle le vrai modèle : ils utilisent un faux modèle scripté. L'évaluation sur de vrais modèles est payante et ne se lance qu'explicitement (voir la [leçon 28](docs/lecons/28-evaluation.md)).

## Architecture

| Partie | Rôle |
|---|---|
| `src/AgentYattus.Api` | Monolithe modulaire ASP.NET Core : modules Missions, Agents, Audit, Llm ; PostgreSQL ; exécution en arrière-plan ; SignalR ; OpenTelemetry ; jetons Keycloak |
| `src/AgentYattus.Contracts` | Objets échangés entre l'API et les clients |
| `src/AgentYattus.Client.Core` | Client HTTP et SignalR, ViewModels partagés (sans dépendance d'interface) |
| `src/AgentYattus.Desktop` | Application Avalonia : Connexion, Accueil, Mission en direct, Agents, Rapports, Paramètres |
| `src/AgentYattus.Admin` | Espace admin web Angular 21 + spartan/ui : Vue d'ensemble, Missions, Audit, Agents, Modèles |
| `deploy/keycloak` | Realm Keycloak de développement (comptes de test) |
| `spikes/AgentLoop` | La boucle d'agent écrite à la main, puis avec Agent Framework |
| `evals/` | Banc d'évaluation : cas, workspace à défauts plantés, coût par mission réussie |

Décisions d'architecture : [`docs/adr/`](docs/adr/). Modèle de sécurité : [`docs/securite.md`](docs/securite.md). Démo : [`docs/demo.md`](docs/demo.md). Feuille de route : [`docs/roadmap.md`](docs/roadmap.md).

## Licence

MIT, voir [`LICENSE`](LICENSE).
