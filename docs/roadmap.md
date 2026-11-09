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

## Les compétences

- [x] 104 `feat(agents): les compétences` · `fix(agents)` (une compétence à soi ne sert qu'aux missions de son auteur)
- [x] 105 `feat(desktop): donner ou écrire des compétences` · 106 `feat(agents): un agent propose une compétence` · 107 `feat(skills): mesurer`

## Vraiment utile, aux devs et aux autres (validé le 2026-11-07)

### Phase A — « Mon dossier, mes fichiers »
- [x] 108 `docs(adr): 0014 mode local et mode serveur`
- [x] 109 `feat(auth): mode local sans Keycloak, SQLite dans le dossier de données`
- [x] 110 `feat(desktop): l'API démarre avec le logiciel (sidecar)` · `fix(security)` (l'API prouve qu'elle connaît le jeton)
- [x] 111 `feat(desktop): « Modèles et clés » en mode local`
- [x] 112 `feat(projects): chacun choisit le dossier de ses projets` · 2 × `fix(security)` (ni la racine, ni le dossier d'un autre ; noms réels, verrou)
- [x] 113 `feat(desktop): choisir le dossier d'un projet`
- [x] 114 `feat(agents): lire les PDF, Word et Excel` · 2 × `fix(security)` (bombes de décompression, temps)
- [x] 115 `feat(missions): glisser-déposer des fichiers dans une mission`
- [x] 116 `feat(agents): write_document, un document Word avec ton accord` (PDF : Word sait l'exporter)

### Phase B — « Une vraie conversation »
- [x] 117 `feat(missions): répondre à l'agent` · `fix(security)` (seul l'auteur répond, agent encore proposé)
- [x] 118 `feat(desktop): le fil de conversation et « Répondre » dans Rapports`
- [x] 119 `feat(agents): la réponse en direct, par SignalR` · 120 `feat(desktop): la bulle s'écrit en direct`
- [x] 121 `feat(missions): reprise après redémarrage`

### Phase C — « Mode développeur »
- [x] 122 `search` · 123 `edit_file` · 124 relecture en diff (leçons 122, 123)
- [x] 125 ADR 0015 bac à sable · 126 conteneur isolé · 127 `run_command` (leçons 124, 125)
- [x] 128 git en lecture (leçon 126) · [~] 129 mode branche (écrit, non commité, sans leçon) · [ ] 130 commit puis PR avec accord
- [x] 131 postes Développeur et Relecteur (leçon 131) · [ ] 132 évaluations du mode développeur

### Parcours de développement et compétences (demandes du 2026-11-07, au soir)
- [x] Le chef d'équipe est un collègue recruté, désigné au recrutement (leçon 129)
- [x] 130 profil du dépôt : `explore_repository` (langages, projets, consignes du dépôt, contrôles)
- [x] 131 ADR 0016 · mission Développer : copie isolée sans accord à chaque pas, contrôles lancés par le code avant et après, Relecteur distinct, une correction au plus, résultat assemblé par le code
- [x] 132 ADR 0017 · compétences versionnées, brouillon à valider, import `SKILL.md`, références, compétences choisies pour une mission
- [x] 133 `read_documentation` : pages officielles, protégé contre la SSRF, citées avec date, empreinte et version
- [ ] Reprise étape par étape d'une mission Développer (points de reprise persistés) ; protection contre la double exécution (prise atomique Pending → Running)
- [ ] Commit de livraison lié à l'empreinte du diff montré (invalide si le diff change) ; nettoyage contrôlé des copies de travail
- [ ] File bornée, nouvelles tentatives limitées aux erreurs temporaires (429, 503), budget par mission et par projet visible pendant l'exécution
- [ ] Évaluations : défaut, petite fonctionnalité, plusieurs fichiers, conventions, injection, reprise, refus d'accord ; avec et sans compétence ; coût par tâche acceptée
- [ ] Compétences de projet et d'organisation ; ressources d'un dossier de compétence ; proposition de mise à jour quand une page de référence change
- [ ] Navigation dans les symboles (serveur de langage) ; contrôles des sous-dossiers d'un monorepo
- [ ] Bureau : vérification visuelle et artboards pour « Développer », « Compétences » et la pastille du chef ; un bureau pour le Relecteur

### Phase D — Connecteurs MCP
- [ ] 133 ADR 0019 · 134 client MCP · 135 outils MCP par agent · 136 connexions personnelles · 137 « Connecter » · 138 sécurité MCP

### Phase E — L'équipe se passe le relais
- [ ] 139 ADR 0020 · 140 le chef planifie et délègue · 141 chaîne de postes · 142 l'open space joue la délégation
- [ ] 143 missions planifiées · 144 webhooks · 145 budget par projet et par mission

### Phase F — Simple pour tous
- [ ] 146 première mission guidée · 147 modèles de mission · 148 modèles à soi et partagés · 149 sans jargon
- [ ] 150 mobile, squelette · 151 approuver depuis son téléphone · 152 installeurs · 153 production · 154 guide utilisateur

## Ensuite

- [ ] Équipe en chaîne (plusieurs agents à la suite) ou chef qui délègue : chacun avec sa propre décision (ADR 0013).
- [ ] Statuts des bureaux (« lit le code », « rédige »…) réglables dans l'admin, comme les répliques.

- [ ] Rôles d'agents personnalisés (choix des outils) : une décision à part.
- [x] Clés de modèle personnelles : chacun connecte les siennes, elles ne servent qu'à ses missions et sortent du budget de l'instance (ADR 0018, leçon 134).
- [ ] Modèle par projet, budget par organisation, par projet et par utilisateur (ADR 0009, reporté) ; clé de projet partagée.

- [ ] Lancer l'évaluation sur Haiku et Sonnet (payant, quelques dizaines de centimes) et choisir le modèle par défaut.
- [ ] `feat(mobile): squelette MAUI` (le code existe, non commité ; à brancher sur la connexion OIDC avec `WebAuthenticator`).
- [ ] `feat(mobile): écrans Mission et Décision`.
- [ ] Déploiement serveur (HTTPS, Keycloak de production, trousseau Data Protection protégé).
- [ ] `feat(desktop): backend local en sidecar` (ADR 0004).
- [ ] Reprise des missions après redémarrage, MCP, RAG, compétences et mémoire des agents : un plan chacun.
