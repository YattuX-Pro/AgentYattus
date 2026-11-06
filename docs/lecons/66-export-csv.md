# Leçon 66 — Exporter l'audit en CSV, sans offrir une formule au tableur

> **Commit** : `feat(audit): export CSV`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Audit/AuditModule.cs`, `Contracts/Missions.cs`, `tests/…/AuditFeedTests.cs`, admin (`core/api.ts`, `pages/audit/audit.ts`)

## 1. Ce qu'on construit

`GET /audit/export?limit=5000` (administrateurs) renvoie un fichier CSV : horodatage, mission, événement. Un bouton **Exporter en CSV** sur la page Audit le télécharge.

## 2. L'injection de formules (CSV injection)

Un tableur (Excel, LibreOffice) **exécute** une cellule qui commence par `=`, `+`, `-` ou `@`. Or le journal contient des noms venus de l'extérieur : un nom d'outil, un identifiant de modèle (venu du catalogue ou d'un serveur local). Une cellule `=HYPERLINK("http://site-piégé", "Cliquez")` deviendrait un lien piégé dans le tableur de l'administrateur.

```csharp
if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
    value = "'" + value;
```

L'apostrophe en tête dit au tableur : « ceci est du texte ». C'est la recommandation de l'OWASP. Puis on met entre guillemets toute cellule qui contient une virgule, un guillemet ou un retour à la ligne, et on double les guillemets internes : c'est la règle du format CSV (RFC 4180).

## 3. Télécharger un fichier protégé

```ts
const response = await this.api.exportAudit();   // HttpClient : le jeton part avec la requête
const url = URL.createObjectURL(response.body!);
Object.assign(document.createElement('a'), { href: url, download: name }).click();
```

Un simple lien `<a href="/api/audit/export">` n'enverrait pas le jeton (il est en mémoire, leçon 39). On télécharge donc le fichier par `HttpClient`, on en fait une adresse locale (`blob:`), et on simule un clic sur un lien de téléchargement.

L'en-tête UTF-8 (BOM) en tête du fichier fait reconnaître les accents à Excel.

## 4. Quiz

1. Qu'est-ce qu'une injection de formules, et d'où viendrait-elle ici ?
2. Pourquoi ne pas mettre un simple lien vers l'export ?

<details>
<summary>Réponses du quiz</summary>

1. Une cellule qui commence par `=`, `+`, `-` ou `@` devient une formule dans un tableur ; un nom d'outil ou de modèle venu de l'extérieur pourrait en contenir une.
2. Le lien n'enverrait pas le jeton, qui vit en mémoire : on télécharge par `HttpClient`, puis on fabrique une adresse locale.

</details>
