# Leçon 116 — Un agent écrit un document Word, avec ton accord

> **Commit** : `feat(agents): write_document, un document Word avec ton accord`
> **Phase** : A
> **Fichiers** : `Api/Modules/Agents/{WorkspaceTools,Documents,AgentRoles,AgentsModule}.cs`, `Contracts/Agents.cs`, `Client.Core/{MissionViewModel,Scene,RecruitViewModel,ApiClient}.cs`, tests

## 1. Ce qu'on construit

Un nouvel outil, `write_document(path, content)`. L'agent écrit un texte simple :

```
# Synthèse
Trois points :
- délais tenus
```

Le code le met en page dans un `.docx` : titre en gras et en grand, puce « • ». Comme `write_file`, l'outil est enveloppé dans `ApprovalRequiredAIFunction`. Tu relis **le texte exact** avant qu'il soit écrit, et l'open space raconte la demande comme pour un fichier.

**Nouveau collègue** propose maintenant tous les outils de l'instance (`GET /agents/tools`), même ceux qu'aucun agent n'a encore : « Écrire un document Word ».

## 2. Pourquoi un texte simple, et pas du XML Word ?

- **Ce que tu approuves, c'est ce qui sera écrit.** Un XML Word de 300 lignes ne se relit pas.
- **Aucune injection.** Le texte est mis tel quel dans des `Run` OpenXml. `<b>` reste `<b>`, et aucun champ Word (macro, lien automatique) ne peut être glissé par le contenu.

## 3. Vérifie toi-même

`dotnet test --filter An_agent_writes_a_word_document` : le document relu par `read_file` rend le même texte. Les autres cas :

- un `.txt` est refusé ;
- `../dehors.docx` reste hors d'atteinte ;
- l'outil exige un accord humain.

## 4. Les limites

- **Pas de PDF.** Word sait exporter en PDF. Un PDF propre (polices, accents, retours à la ligne) demanderait une bibliothèque de mise en page.
- **Mise en page minimale** : titres, puces, paragraphes. Ni tableaux, ni images.

## 5. Quiz

1. Pourquoi l'humain relit-il le texte, et pas le fichier .docx ?

<details>
<summary>Réponse</summary>

1. Le .docx est une archive de XML illisible. Le texte est exactement ce qui sera mis en page, et le code de mise en page n'ajoute rien qui vienne du modèle.

</details>
