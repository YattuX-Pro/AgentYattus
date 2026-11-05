# Leçon 07 — L'intégration continue avec GitHub Actions

> **Commit** : `ci: build et tests sur GitHub Actions`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `.github/workflows/ci.yml`

## 1. Ce qu'on construit

Un **robot** qui, à chaque envoi de code sur GitHub, compile le projet et lance tous les tests sur une machine neuve. Si quelque chose casse, on le voit tout de suite, en rouge.

> **Une image** : un contrôleur qualité en fin de chaîne. Chaque pièce qui sort de l'atelier passe devant lui. Une pièce défectueuse est arrêtée avant d'arriver chez le client.

## 2. Le problème

- « Ça marche sur ma machine » : un fichier oublié, un SDK différent, et le code ne compile pas ailleurs.
- On oublie de lancer les tests avant d'envoyer son code.
- Plus tard, une contribution externe pourrait casser quelque chose sans que personne ne s'en aperçoive.

## 3. Le fichier expliqué

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
```

**Quand** le robot se déclenche : à chaque envoi sur `main`, et à chaque *pull request* (une proposition de modification).

```yaml
jobs:
  build-and-test:
    runs-on: ubuntu-latest
```

**Où** il travaille : une machine Linux neuve, fournie par GitHub, effacée après chaque passage.

```yaml
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
```

1. `checkout` : télécharger le code.
2. `setup-dotnet` : installer **exactement** le SDK demandé dans `global.json` (leçon 01). La CI compile donc comme toi.

```yaml
      - run: dotnet build spikes/AgentLoop.Tests
      - run: dotnet test --project spikes/AgentLoop.Tests --no-build
```

3. Compiler.
4. Lancer les tests. `--no-build` évite de recompiler ce qui vient de l'être.

Si **une seule** étape échoue, le passage entier est marqué en **rouge**, et GitHub l'affiche sur la pull request.

Ce fichier grandira à chaque commit : l'API, le desktop et leurs tests y seront ajoutés au fur et à mesure.

## 4. Pourquoi ces choix

| Choix | Raison |
|---|---|
| GitHub Actions | Le code est sur GitHub, c'est intégré et gratuit pour les dépôts publics. |
| Linux | Rapide et le moins cher ; .NET y tourne parfaitement. |
| Le SDK lu dans `global.json` | Une seule source de vérité pour la version du SDK. |
| Les tests à chaque pull request | Une erreur est arrêtée **avant** d'entrer dans `main`. |

Les tests sur le vrai Claude ne sont **pas** lancés par la CI : ils coûteraient de l'argent à chaque passage, et il faudrait confier la clé d'API au robot. Le faux modèle suffit à vérifier la boucle.

## 5. Vérifie toi-même

Une fois le dépôt publié sur GitHub, ouvre l'onglet **Actions** : chaque envoi de code y apparaît, avec une coche verte ou une croix rouge.

En local, la même vérification :

```bash
dotnet test --project spikes/AgentLoop.Tests
```

## 6. Quiz

1. Quand la CI se déclenche-t-elle ?
2. Pourquoi la machine de la CI est-elle neuve à chaque passage ?
3. Pourquoi la CI n'appelle-t-elle pas le vrai Claude ?

## 7. Pour aller plus loin

- Build et tests .NET sur GitHub Actions : <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>

<details>
<summary>Réponses du quiz</summary>

1. À chaque envoi sur la branche `main`, et à chaque pull request.
2. Pour que rien de ta machine (un fichier oublié, un réglage local) ne fasse croire que le code fonctionne.
3. Parce que ce serait payant à chaque passage et qu'il faudrait confier la clé au robot. Le faux modèle vérifie la boucle gratuitement.

</details>
