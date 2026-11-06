# ADR 0008 — Un espace d'administration web, séparé des apps de travail

- **Statut** : proposé
- **Date** : 2026-11-06

## Contexte

Jusqu'ici, l'app de bureau fait tout : lancer une mission, la suivre, approuver une écriture, mais aussi connecter des fournisseurs, choisir les modèles, régler le mode Gratuit. L'app mobile suit le même chemin.

Agent Yattus va servir à une équipe. Gérer l'instance (modèles, agents, budget, utilisateurs, audit) n'est pas le travail quotidien de chacun : c'est celui d'un ou deux administrateurs. Mélanger les deux alourdit les apps de travail et oblige à tout refaire trois fois (bureau, mobile, et demain le web).

## Décision

1. **Séparation des responsabilités.**
   - **L'espace admin web** gère tout : vue d'ensemble, missions (surveiller, décider, arrêter), audit, agents, modèles, puis (phase suivante) projets, organisation et utilisateurs.
   - **Le bureau et le mobile** servent à **travailler** : lancer une mission, la suivre, approuver ses écritures, lire les rapports.
   - L'écran Modèles du bureau reste en place tant que l'admin web n'est pas livré ; il sera retiré ensuite.
2. **Angular + spartan/ui.** Angular parce que c'est le framework que l'équipe maîtrise ; [spartan/ui](https://www.spartan.ng) parce qu'il suit le modèle de shadcn : des primitives accessibles (Brain, un paquet npm) et des composants stylés **copiés dans le projet** (Helm), qu'on adapte à la palette d'Agent Yattus au lieu de lutter contre un thème imposé. Tailwind 4 porte les jetons de couleur, en clair et en sombre.
3. **Angular 21 (LTS)** pour l'instant : Angular 22.2 exige Node 24.15, la machine de développement a Node 24.13. On passera à 22 avec la mise à jour de Node ; spartan 1.9 accepte les deux.
4. **Projet `src/AgentYattus.Admin`**, à côté des projets .NET, avec sa propre CI (build et tests Vitest).
5. **Connexion par Keycloak**, comme le bureau : code d'autorisation avec PKCE, nouveau client public `agent-yattus-admin`. Les jetons restent **en mémoire** (jamais dans `localStorage`, lisible par n'importe quel script de la page). Recharger la page reconnecte sans saisie grâce à la session Keycloak.
6. **Même origine que l'API en développement** : le serveur de dev d'Angular relaie `/llm`, `/missions`, `/agents`, `/audit` vers l'API. L'API reste sur la boucle locale et n'a **pas besoin de CORS**.
7. **L'accès est réservé au rôle `llm-admin`** en attendant les vrais rôles (Administrateur, Opérateur, Observateur) de la phase multi-utilisateur. Le contrôle dans l'écran n'est qu'un confort : **c'est l'API qui refuse**, route par route.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Tout garder dans le bureau | Rien à créer | Le mobile et le web devraient tout refaire ; un utilisateur voit des réglages qui ne le concernent pas |
| Blazor | Même langage que l'API, partage des contrats C# | Moins mûr pour une interface riche ; l'équipe connaît mieux Angular |
| Angular + Angular Material | Complet, connu | Style Material difficile à plier à notre identité |
| **Angular + spartan/ui** | Composants accessibles qu'on possède et adapte ; Tailwind | Bibliothèque plus jeune ; on maintient le code copié |
| Jetons dans `localStorage` | Survivent au rechargement | Volables par n'importe quel script injecté (XSS) |
| CORS sur l'API | Le front peut être servi n'importe où | Une surface de plus à configurer et à auditer ; inutile en développement |

## Conséquences

- Un deuxième écosystème dans le dépôt (Node, npm) et un job de CI de plus.
- Les contrats ne sont plus partagés automatiquement : les types TypeScript recopient les DTO C#. Une génération depuis OpenAPI viendra si l'écart devient pénible.
- Le déploiement devra servir l'admin et l'API sous la même origine (reverse proxy), ou ajouter CORS à ce moment-là.
