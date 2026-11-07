# Leçon 115 — Glisser des fichiers dans une mission

> **Commit** : `feat(missions): glisser-déposer des fichiers dans une mission`
> **Phase** : A
> **Fichiers** : `Contracts/Projects.cs`, `Api/Modules/Projects/ProjectsModule.cs`, `Client.Core/{ApiClient,HomeViewModel}.cs`, `Desktop/Views/HomeView.axaml(.cs)`, `tests/AttachmentsTests.cs`, `tests/.../HomeViewModelTests.cs`

## 1. Ce qu'on construit

- **Glisser un fichier sur le composeur**, ou cliquer sur le trombone, le **copie dans le dossier du projet**, sous `pieces-jointes/`. Une puce apparaît : « devis.pdf ×».
- **Au lancement**, le but envoyé se termine par la liste des pièces jointes :

```
Résume ce devis

Pièces jointes (dans le dossier du projet, à lire avec read_file) :
- pieces-jointes/devis.pdf
```

L'agent les lit avec `read_file`, en texte, même un PDF (leçon 114). Il n'y a pas de nouvel outil, ni de nouveau stockage.

## 2. Pourquoi passer par l'API, même en local ?

En mode serveur, le logiciel ne peut pas écrire dans le dossier du serveur : le fichier part donc en `multipart/form-data` vers `POST /projects/{id}/attachments`. En mode local, c'est le même chemin : un seul code.

## 3. Ce que l'API vérifie

- **Le droit de lancer des missions dans le projet** : propriétaire ou éditeur. Un lecteur reçoit 403 ; sans accès, 404.
- **Un dossier choisi** pour le projet (leçon 113).
- **20 Mo au plus**, et jamais vide.
- **Le nom seul** : `../../.env` devient `env`. Pas de dossier, pas de point au début (fichier caché), pas de caractère invisible ou réservé, 100 caractères au plus.
- **Jamais d'écrasement** : `devis.pdf`, puis `devis (2).pdf` (`FileMode.CreateNew`).
- **`pieces-jointes` ne doit pas être un lien symbolique** : un dépôt cloné pourrait en contenir un qui mène ailleurs.

## 4. Vérifie toi-même

- `dotnet test --filter AttachmentsTests` : sans dossier, 400 ; deux `devis.pdf` ; un `../../.env` qui reste dans `pieces-jointes/` ; Bob sans accès.
- `dotnet test --filter Attached_files_are_uploaded` : le but envoyé contient la liste.

## 5. Les limites

- Retirer une puce ne supprime pas le fichier copié : il reste dans `pieces-jointes/`.
- Les puces et le trombone reprennent les composants du composeur, sans artboard.

## 6. Quiz

1. Pourquoi refuser un nom qui commence par un point ?

<details>
<summary>Réponse</summary>

1. Ce serait un fichier caché (`.env`, `.git…`), qui peut changer la configuration d'outils lancés dans ce dossier, ou masquer ce qu'on y dépose.

</details>
