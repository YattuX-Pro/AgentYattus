# Feuille de route

Un commit correspond à une étape vérifiable. On coche une ligne quand sa vérification passe **et** qu'elle est commitée.
« Squelette » : le code existe et sa vérification passe, mais rien n'est encore commité.

Méthode (depuis le 5 novembre 2026) : Claude écrit le code, commit par commit. Chaque commit est accompagné
d'une leçon pour débutant dans `docs/lecons/NN-nom.md` : ce qui a été construit, pourquoi, comment le vérifier, et un quiz.
À la fin, les leçons sont rassemblées dans un PDF qui sert de support de cours. Les mentions « toi » et « ensemble »
ci-dessous datent de l'ancienne méthode.

Calendrier indicatif (4 à 5 mois depuis le 24/10/2026) : MVP démontrable mi-février 2027, post-MVP jusqu'à mi-mars.

## Phase 0 — Socle et boucle d'agent (→ 8 nov.)

- [ ] `chore: socle .NET 10` (squelette).
- [ ] `docs: ADR 0001, clients Avalonia et MAUI` (squelette).
- [ ] `feat(spike): boucle d'agent écrite à la main` (**toi**, `RunAsync`). Vérification : 17/17.
- [ ] `docs(learning): 01, la boucle d'agent` (**toi**).
- [ ] Lecture guidée du squelette (**toi**) : je pose les questions, tu réponds en lisant le code. Vérification : tu expliques le trajet d'une mission, de `POST /missions` jusqu'à la carte « Terminée ».
- [ ] `docs: ADR 0002, licence du projet` (**toi**). Avant de publier le dépôt.
- [ ] `ci: build et tests sur GitHub Actions` (squelette). Vérification : pipeline vert sur une pull request.

## Phase 1 — Agent Framework (→ 22 nov.)

- [ ] `feat(spike): la même mission avec Agent Framework` (**ensemble**). Vérification : les mêmes scénarios passent.
- [ ] `docs: ADR 0003, architecture backend` (**toi** : décrire ce que le squelette a choisi et pourquoi).
- [ ] `docs: ADR 0004, backend hybride local/serveur` (**toi**).

## Phase 2 — Backend en monolithe modulaire (→ 20 déc.)

- [ ] `feat(api): hôte, /health, PostgreSQL, missions, exécuteur, audit, hub SignalR` (squelette, revu par le lead).
- [ ] `test(architecture): frontières entre modules` (squelette).
- [ ] `feat(agents): vrai runner sur Agent Framework` (**toi**, je relis ; reprend `WorkspaceTools`, remplace le runner provisoire). Vérification : tests avec `ScriptedChatClient`, puis une vraie mission Haiku.
- [ ] `feat(missions): GET /missions` (**toi** : premier endpoint, sur le modèle de `GET /missions/{id}` ; la liste, pour que les clients ne montrent plus seulement leurs missions).
- [ ] `chore(db): migrations EF Core` (avant le premier changement de schéma).
- [ ] `feat(approvals): pause et reprise sur action sensible` (**ensemble**). La mission en pause libère l'exécuteur. Vérification : approuver écrit le fichier, refuser n'écrit rien.
- [ ] `feat(observability): OpenTelemetry` (**ensemble**). Vérification : tokens et durée d'un appel visibles.

## Phase 3 — Client desktop (→ 31 janv.)

- [ ] `feat(client-core): ApiClient, hub, HomeViewModel` (squelette).
- [ ] `feat(desktop): coquille, jetons, écran Accueil` (squelette, conforme à la maquette).
- [ ] `feat(desktop): écran Mission en direct` (graphe des agents, frise, SignalR).
- [ ] `feat(desktop): carte d'approbation` (**toi**).
- [ ] `feat(desktop): écrans Agents et Rapport`.

## Phase 4 — MVP (→ 14 févr.)

- [ ] `feat: mission de revue à deux agents` (**toi** : prompts des rôles et passage de relais ; Analyste puis Rédacteur, REPORT.md soumis à approbation).
- [ ] `test(eval): coût par mission réussie, par modèle` (**toi**, Haiku / Sonnet).
- [ ] `docs: README et démo du MVP`. Vérification : la démo va de bout en bout, approbation comprise.

## Après le MVP (→ mi-mars)

- [ ] `feat(mobile): squelette MAUI, accueil` (squelette, Android et iOS).
- [ ] `feat(mobile): écrans Mission et Décision`.
- [ ] `feat(auth)`, puis déploiement serveur.
- [ ] `feat(desktop): backend local en sidecar` (selon l'ADR 0004).
- [ ] Reprise des missions après redémarrage, MCP, RAG : un plan chacun.
