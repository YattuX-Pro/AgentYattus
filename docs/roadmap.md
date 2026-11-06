# Feuille de route

Un commit correspond à une étape vérifiable : son code, ses tests, et sa leçon pour débutant dans `docs/lecons/NN-nom.md`.
Une ligne est cochée quand sa vérification passe **et** qu'elle est commitée.

Méthode (depuis le 5 novembre 2026) : Claude écrit le code, phase par phase. Chaque commit est vérifié seul, sur une copie propre
du dépôt. À la fin, les leçons sont rassemblées dans un PDF qui sert de support de cours.

Calendrier indicatif (4 à 5 mois depuis le 24/10/2026) : MVP démontrable mi-février 2027, post-MVP jusqu'à mi-mars.

## Phase 0 — Socle et boucle d'agent

- [x] 01 `chore: socle .NET 10`
- [x] 02 `docs: ADR 0001, clients Avalonia et MAUI`
- [x] 03 `feat(spike): outils de fichiers confinés au workspace`
- [x] 04 `feat(spike): boucle d'agent écrite à la main`
- [x] 05 `feat(spike): connexion à Claude Haiku`
- [x] 06 `docs: ADR 0002, licence MIT`
- [x] 07 `ci: build et tests sur GitHub Actions`

## Phase 1 — Agent Framework

- [x] 08 `feat(spike): la même mission avec Agent Framework`
- [x] 09 `docs: ADR 0003, architecture backend`
- [x] 10 `docs: ADR 0004, backend hybride local/serveur`

## Phase 2 — Backend

- [x] 11 `feat(api): hôte, /health et contrats`
- [x] 12 `feat(missions): PostgreSQL, créer et lire une mission`
- [x] 13 `feat(missions): exécution en arrière-plan`
- [x] 14 `feat(audit): journal des événements`
- [x] 15 `feat(api): temps réel avec SignalR`
- [x] 16 `test(architecture): frontières entre modules`
- [x] 17 `feat(agents): vrai runner sur Agent Framework` (avec le refus des liens symboliques)
- [x] 18 `feat(missions): GET /missions`
- [x] 19 `chore(db): migrations EF Core`
- [x] 20 `feat(approvals): pause et reprise sur action sensible`
- [x] 21 `feat(observability): OpenTelemetry`

## Phase 3 — Client desktop

- [x] 22 `feat(client-core): client API, hub et ViewModel d'accueil`
- [x] 23 `feat(desktop): coquille, jetons de design et écran Accueil`
- [x] 24 `feat(desktop): écran Mission en direct`
- [x] 25 `feat(desktop): carte d'approbation`, puis `fix(approvals)` (décision liée à la demande affichée, lecture complète exigée)
- [x] 26 `feat(desktop): écrans Agents et Rapports`

## Phase 4 — MVP

- [x] 27 `feat: mission de revue à deux agents`
- [x] 28 `test(eval): coût par mission réussie, par modèle` (l'évaluation payante reste à lancer)
- [x] 29 `docs: sécurité, README et démo du MVP`

## Authentification et gestion du LLM

- [x] 30 `feat(auth): Keycloak et API protégée par jeton` (ADR 0005)
- [x] 31 `feat(desktop): connexion OpenID Connect avec PKCE`
- [x] 32 `feat(llm): clés d'API chiffrées, choix du modèle et révocation` (ADR 0006)
- [x] 33 `feat(desktop): écran Paramètres, modèle et clés d'API`

## Fournisseurs et modèles, comme opencode

- [x] 34 `feat(llm): plusieurs fournisseurs et le catalogue models.dev` (ADR 0007)
- [x] 35 `feat(llm): un modèle par agent`

## Ensuite

- [ ] Lancer l'évaluation sur Haiku et Sonnet (payant, quelques dizaines de centimes) et choisir le modèle par défaut.
- [ ] `feat(mobile): squelette MAUI` (le code existe, non commité ; à brancher sur la connexion OIDC avec `WebAuthenticator`).
- [ ] `feat(mobile): écrans Mission et Décision`.
- [ ] Déploiement serveur (HTTPS, Keycloak de production, trousseau Data Protection protégé).
- [ ] `feat(desktop): backend local en sidecar` (ADR 0004).
- [ ] Reprise des missions après redémarrage, MCP, RAG, compétences et mémoire des agents : un plan chacun.
- [ ] Rassembler les leçons dans le PDF du cours.
