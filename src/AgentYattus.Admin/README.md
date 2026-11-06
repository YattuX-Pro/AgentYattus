# Agent Yattus · Admin

L'espace d'administration web (ADR 0008) : Angular 21, [spartan/ui](https://www.spartan.ng), Tailwind 4.

```bash
npm ci
npm start        # http://localhost:4200, relaie /api vers l'API (http://localhost:5080)
npm test         # Vitest
```

Prérequis : l'API (`dotnet run --project src/AgentYattus.Api`) et Keycloak (`docker compose up keycloak`).

Les composants spartan (Helm) sont copiés dans `src/app/ui` : on les modifie ici, ils nous appartiennent.
