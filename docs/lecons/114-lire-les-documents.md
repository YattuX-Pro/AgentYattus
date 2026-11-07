# Leçon 114 — Lire les PDF, les Word et les Excel

> **Commit** : `feat(agents): lire les PDF, Word et Excel`
> **Phase** : A
> **Fichiers** : `Api/Modules/Agents/{Documents,WorkspaceTools}.cs`, `Directory.Packages.props` (PdfPig, DocumentFormat.OpenXml), `tests/DocumentsTests.cs`

## 1. Ce qu'on construit

`read_file` savait lire du texte. Il convertit maintenant en texte :

| Fichier | Rendu |
|---|---|
| `.pdf` | « --- Page 1 --- » puis le texte de la page, pour chaque page |
| `.docx` | Un paragraphe par ligne ; un tableau, une rangée par ligne, cellules séparées par des tabulations |
| `.xlsx` | « --- Feuille « 2026 » --- » puis une ligne par rangée, cellules séparées par des tabulations |

Aucun nouvel outil : l'agent appelle `read_file("devis.pdf")`, comme pour un `.cs`. Sa description le lui dit.

## 2. Les bibliothèques

- **PdfPig** (Apache 2.0) lit le texte des PDF, sans dépendance native.
- **DocumentFormat.OpenXml** (MIT, Microsoft) lit `.docx` et `.xlsx`, qui sont des archives zip de XML.

## 3. Tout est borné

Un document peut être hostile, comme n'importe quel fichier du workspace :

- **Taille sur le disque** : 20 Mo au plus (`MaxDocumentBytes`).
- **Bombe de décompression** : un `.docx` de 50 Ko peut contenir une partie de plusieurs Go une fois décompressée. `OpenSettings.MaxCharactersInPart` refuse toute partie au-delà de 20 millions de caractères.
- **Texte rendu** : coupé à la même limite que les autres fichiers (`MaxFileKb`, admin › Réglages), avec une note « texte coupé ». L'agent sait qu'il manque quelque chose.
- **Pages et rangées** : 300 pages et 5 000 rangées par feuille au plus.
- **Document cassé** : « Document illisible : faux.pdf », sans le détail de l'exception.

**Après revue de sécurité** (un correctif à part) :

- **Taille totale** : avant d'ouvrir un `.docx` ou un `.xlsx`, on le **décompresse pour de bon**, sans rien garder, en comptant les octets réellement produits : 100 Mo et 2 000 entrées au plus. Les tailles annoncées dans l'archive peuvent mentir, ce compte-là non. Une deuxième revue l'a demandé : la première version se fiait aux tailles annoncées.
- **Par partie** : 5 millions de caractères au plus, une fois chargée par OpenXml.
- **Nombre de feuilles** : 50 au plus.
- **Temps** : 15 secondes par document, vérifiées entre deux pages ou deux rangées, avec le jeton d'arrêt de la mission. Au-delà, on rend ce qui a été lu, avec une note.
- **Limite connue** (`ponytail`) : une seule page PDF énorme peut encore consommer mémoire et processeur avant la vérification suivante. Une lecture dans un processus séparé, avec un plafond de mémoire, viendra avec le bac à sable (phase C).

Et toujours : le texte d'un document est une **donnée**, jamais une consigne (leçon 03). Un PDF qui écrit « ignore tes règles » reste un PDF.

## 4. Vérifie toi-même

`dotnet test --filter DocumentsTests` : un PDF de deux pages, un contrat Word avec un tableau, un classeur Excel, un document trop long et un faux PDF.

## 5. Les limites

- **Pas d'OCR.** Un PDF scanné, fait d'images, rend un texte vide.
- **Les formules Excel** rendent leur dernière valeur calculée, pas la formule. Les dates rendent leur nombre de jours.
- Pas encore de `.pptx` ni d'anciens formats (`.doc`, `.xls`).

## 6. Quiz

1. Pourquoi un `.docx` de 50 Ko peut-il être dangereux ?
2. Pourquoi ajouter une note quand le texte est coupé ?

<details>
<summary>Réponses du quiz</summary>

1. C'est une archive zip : une fois décompressée, une partie peut peser des Go et remplir la mémoire du serveur.
2. Sinon, l'agent croirait avoir tout lu et conclurait sur une partie du document.

</details>
