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
- **Plusieurs utilisateurs** : organisations (Administrateur, Opérateur, Observateur), projets privés, partagés à des personnes ou ouverts à l'organisation ; chaque mission appartient à un projet, et l'on ne voit que ce qu'on a le droit de voir (ADR 0009).
- **Espace admin web** (Angular + spartan/ui) : vue d'ensemble avec le coût du mois, projets, missions en direct (approuver, arrêter, relancer), audit exportable en CSV, agents (consignes versionnées, et **nouveaux agents** créés avec des outils choisis dans la liste, ADR 0013), modèles, organisations, utilisateurs, réglages (budget, durée, missions simultanées, fichiers exclus, rétention). Le bureau et le mobile servent à travailler ; la gestion se fait sur le web (ADR 0008).
- **Le bureau des agents** (ADR 0012) : une mission se suit comme un **open space**. Les agents sont des collègues au visage réaliste, avec un prénom et une personnalité ; ils se parlent dans des bulles qui s'écrivent, se passent des dossiers, et frappent à ta porte quand ils ont besoin de ton accord. On recrute un collègue depuis le bureau (galerie de 22 portraits). Visages, équipe de départ, traits, répliques et rythme de la scène se gèrent dans l'admin : rien n'est en dur.
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

Le bureau s’ouvre directement sur le serveur par défaut, sans choix de stockage. Les adresses du serveur configuré sont réutilisées ; les variables `AGENTYATTUS_API_URL`, `AGENTYATTUS_AUTHORITY` et `AGENTYATTUS_ADMIN_URL` permettent de les remplacer. Le mode local reste disponible avec `AGENTYATTUS_MODE=local`. Les deux modes conservent des données distinctes.

Les missions utilisent des lectures ciblées, des compétences chargées à la demande et des plafonds de contexte, de sortie et de tokens par mission. Voir [Choisir la base et limiter les tokens](docs/base-et-economie-tokens.md) pour les réglages et leurs limites.

Pour créer un agent sans savoir rédiger ses consignes ou ses compétences, ouvrir **Nouveau collègue › Agent RH** : décrire le besoin, répondre aux questions utiles, puis créer le profil proposé ou ajuster sa fiche. Voir [Recruter avec l'agent RH](docs/recrutement-rh.md).

Espace admin web (Node 24) :

```bash
cd src/AgentYattus.Admin && npm ci && npm start
```

Puis <http://localhost:4200>, compte `dev` / `agent-yattus-dev`.

Dans l'espace admin : **Modèles › Connecter un fournisseur** (une clé d'API, ou un serveur local), puis choisir le modèle ; **Projets** pour choisir le dossier de travail de chaque projet. Les clés sont chiffrées sur le serveur et ne sont plus jamais affichées. L'application de bureau (compte `dev` / `agent-yattus-dev`, voir [`deploy/keycloak/README.md`](deploy/keycloak/README.md)) sert à lancer et suivre les missions.

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
| `src/AgentYattus.Api` | Monolithe modulaire ASP.NET Core : modules Identity, Projects, Missions, Agents, Audit, Llm, Settings, Team ; PostgreSQL ; exécution en arrière-plan ; SignalR ; OpenTelemetry ; jetons Keycloak |
| `src/AgentYattus.Contracts` | Objets échangés entre l'API et les clients |
| `src/AgentYattus.Client.Core` | Client HTTP et SignalR, ViewModels partagés (sans dépendance d'interface) |
| `src/AgentYattus.Desktop` | Application Avalonia pour travailler : Connexion, Accueil (avec le choix du projet), l'open space d'une mission, Nouveau collègue, Agents, Rapports ; la gestion s'ouvre dans l'espace admin |
| `src/AgentYattus.Admin` | Espace admin web Angular 21 + spartan/ui : Vue d'ensemble, Projets, Missions, Audit, Agents, Modèles, Portraits, Traits et répliques, Organisations, Utilisateurs, Équipes, Réglages |
| `deploy/keycloak` | Realm Keycloak de développement (comptes de test, thème de connexion, Google et GitHub) |
| `deploy/portraits` | Galerie de portraits importée au premier démarrage |
| `spikes/AgentLoop` | La boucle d'agent écrite à la main, puis avec Agent Framework |
| `evals/` | Banc d'évaluation : cas, workspace à défauts plantés, coût par mission réussie |

Décisions d'architecture : [`docs/adr/`](docs/adr/). Modèle de sécurité : [`docs/securite.md`](docs/securite.md). Démo : [`docs/demo.md`](docs/demo.md). Feuille de route : [`docs/roadmap.md`](docs/roadmap.md).

## Licence

MIT, voir [`LICENSE`](LICENSE).
