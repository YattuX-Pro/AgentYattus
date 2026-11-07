# Leçon 105 — Les compétences sur « Nouveau collègue »

> **Commit** : `feat(desktop): donner ou écrire des compétences depuis « Nouveau collègue »`
> **Phase** : les compétences
> **Fichiers** : `Client.Core/{ApiClient,RecruitViewModel}.cs`, `Desktop/Views/RecruitView.axaml`, `tests/.../RecruitViewModelTests.cs`

## 1. Ce qu'on construit

Une carte **Compétences** sous « Personnalité » et « Ce qu'il ou elle a le droit de faire » :

- **Les compétences disponibles**, en puces à cocher : les tiennes et celles de l'instance. Au survol, une puce montre quand s'en servir et combien de fois un agent l'a chargée.
- **« Écrire une compétence »** ouvre trois champs : le nom, quand s'en servir, la procédure. Enregistrée, la compétence t'appartient et rejoint la liste, déjà cochée.
- **Recruter** envoie les compétences cochées avec le collègue (`RecruitRequest.Skills`).

## 2. Le problème

Il fallait gérer les compétences sans passer par l'espace admin, réservé au propriétaire de l'instance (leçon 103). L'endroit naturel est l'écran où l'on règle déjà le collègue.

## 3. Les choix

- **Mêmes composants que le reste du formulaire** : les puces des traits (`ToggleButton.trait`), les champs (`TextBox.field`, `TextBox.consigne`) et les cartes. La maquette n'a pas encore cette carte : un artboard reste à dessiner. On réutilise donc ses pièces plutôt que d'en inventer.
- **Cocher une compétence ne crée pas d'agent à soi.** `IsPersonalized` ne regarde que la consigne, les outils et le modèle. La compétence va sur le collègue, pas sur l'agent.
- **Le formulaire garde 876 px**, comme la maquette. La rangée « Personnalité / outils » rétrécit jusqu'à son contenu (`MinHeight="340"`), pour que les compétences restent visibles. Au-delà, seul le formulaire défile.

## 4. Vérifie toi-même

- `dotnet test --filter Checked_and_newly_written_skills_go_with_the_colleague`.
- **Cas d'échec** : une procédure de moins de 20 caractères laisse le bouton « Enregistrer » grisé.
- Dans le logiciel : Agents › le crayon d'un agent › coche une compétence › « Enregistrer ce visage ».

## 5. Les limites

- Une compétence ne se modifie ni ne se retire encore depuis le logiciel (l'API le permet : `PUT` et `DELETE /skills/{clé}`).
- Le badge n'affiche pas les compétences.

## 6. Quiz

1. Pourquoi cocher une compétence ne crée-t-il pas un « Analyste de Nora » ?
2. Pourquoi le bouton « Enregistrer » vérifie-t-il les longueurs, alors que le serveur le fait aussi ?

<details>
<summary>Réponses du quiz</summary>

1. La compétence est rangée sur le collègue (le siège du projet), pas sur l'agent : l'Analyste partagé reste le même.
2. Pour répondre tout de suite, sans aller-retour. Le serveur reste la vraie barrière : le client peut être contourné.

</details>
