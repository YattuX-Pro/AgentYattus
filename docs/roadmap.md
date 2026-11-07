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
- [x] 36 `feat(llm): mode Gratuit, chaîne de modèles et bascule automatique`

## Espace admin web

- [x] 37 `docs(adr): espace d'administration web, séparé des apps de travail` (ADR 0008)
- [x] 38 `feat(admin): projet Angular avec spartan/ui, thème clair et sombre, CI`
- [x] 39 `feat(admin): connexion Keycloak (PKCE), garde admin et relais vers l'API`
- [x] 40 `feat(admin): coque avec menu latéral déroulant, recherche et thème`
- [x] 41 `feat(admin): page Modèles (fournisseurs, défaut, mode Gratuit, catalogue)`
- [x] 42 `feat(admin): page Agents (modèle par rôle, outils et garde-fous)`
- [x] 43 `feat(admin): pages Missions et Audit, journal de toutes les missions`
- [x] 44 `feat(admin): vue d'ensemble (indicateurs, graphique, décisions, activité)`

## Plusieurs utilisateurs

- [x] 45 `docs(adr): plusieurs utilisateurs, organisations, projets et partage` (ADR 0009)
- [x] 46 `feat(identity): annuaire des utilisateurs, organisations et rôles`
- [x] 47 `feat(projects): projets, visibilité, invités et règle d'accès`
- [x] 48 `feat(missions): chaque mission appartient à un projet et à son auteur`
- [x] 49 `feat(admin): page Projets (visibilité, invités, dossier de travail)`
- [x] 50 `feat(admin): pages Organisations et Utilisateurs, groupe Administration`

## Compléter l'admin

- [x] 51 `docs(adr): coût des missions, budget et réglages de l'instance` (ADR 0010)
- [x] 52 `feat(missions): tokens et coût enregistrés par mission`
- [x] 53 `feat(settings): module Réglages de l'instance`
- [x] 54 `feat(settings): budget mensuel et blocage au plafond`
- [x] 55 `feat(audit): purge selon la rétention, exclusions comparées au nom réel`, puis `fix(agents)` (nom exact du disque)
- [x] 56 `feat(admin): page Réglages`
- [x] 57 `feat(admin): coût et tokens dans la vue d'ensemble et les missions`
- [x] 58 `feat(missions): arrêter une mission`
- [x] 59 `feat(missions): relancer une mission`
- [x] 60 `feat(admin): arrêter et relancer depuis l'admin`
- [x] 61 `feat(agents): statistiques par agent`
- [x] 62 `docs(adr): consignes des agents modifiables` (ADR 0011)
- [x] 63 `feat(agents): consignes et tours modifiables, avec historique`
- [x] 64 `feat(admin): modifier un agent`
- [x] 65 `feat(admin): missions en direct par SignalR`
- [x] 66 `feat(audit): export CSV`
- [x] 67 `feat(admin): activité du jour de la chaîne gratuite`
- [x] 68 `refactor(desktop): retirer la gestion des modèles du bureau`
- [x] 69 `feat(desktop): choisir le projet d'une mission`

## Le bureau des agents

Préalable : `feat(auth): page de connexion aux couleurs d'Agent Yattus, avec Google et GitHub`.

- [x] 71 `docs(adr): ADR 0012, les collègues`
- [x] 72 `feat(team): module Équipe et galerie de portraits`
- [x] 73 `feat(team): équipes des projets, traits, répliques et réglages`
- [x] 74 `feat(team): import de la galerie au premier démarrage`
- [x] 75 `feat(agents): le prénom et les traits du collègue dans sa consigne`
- [x] 76 `feat(missions): le rôle et le fichier visé dans le journal`
- [x] 77 `feat(client): traduire le journal d'une mission en répliques`
- [x] 78 `feat(client): OfficeViewModel, la scène qui se joue`
- [x] 79 `feat(admin): transitions sur chaque clic et chaque page, placeholders partout`
- [x] 80 `feat(admin): menu latéral refait et animé`
- [x] 81 `feat(admin): page Portraits`
- [x] 82 `feat(admin): page Équipes`
- [x] 83 `feat(admin): page Traits et répliques`
- [x] 84 à 89 `feat(desktop)`: l'open space, les collègues, la bulle, ce qui circule, on frappe à votre porte, le fil de l'équipe
- [x] 90 `feat(client): RecruitViewModel` · 91 `feat(desktop): l'écran Nouveau collègue`
- [x] `fix(evals)` · 92 `fix(security)` (revue de la phase)
- [x] 94 `docs(adr): ADR 0013, ajouter des agents depuis l'admin`
- [x] 95 `feat(agents): ajouter, modifier et retirer des agents` · `fix(missions)` (relance et agent retiré)
- [x] 96 `feat(admin): page Agents allégée, créer et retirer un agent`
- [x] 97 `feat(desktop): choisir l'agent d'une mission`

## Ensuite

- [ ] Équipe en chaîne (plusieurs agents à la suite) ou chef qui délègue : chacun avec sa propre décision (ADR 0013).
- [ ] Statuts des bureaux (« lit le code », « rédige »…) réglables dans l'admin, comme les répliques.

- [ ] Rôles d'agents personnalisés (choix des outils) : une décision à part.
- [ ] Clés de modèle personnelles (une clé ne sert qu'aux projets de son propriétaire), modèle par projet, budget par organisation et par projet (ADR 0009, reporté).

- [ ] Lancer l'évaluation sur Haiku et Sonnet (payant, quelques dizaines de centimes) et choisir le modèle par défaut.
- [ ] `feat(mobile): squelette MAUI` (le code existe, non commité ; à brancher sur la connexion OIDC avec `WebAuthenticator`).
- [ ] `feat(mobile): écrans Mission et Décision`.
- [ ] Déploiement serveur (HTTPS, Keycloak de production, trousseau Data Protection protégé).
- [ ] `feat(desktop): backend local en sidecar` (ADR 0004).
- [ ] Reprise des missions après redémarrage, MCP, RAG, compétences et mémoire des agents : un plan chacun.
