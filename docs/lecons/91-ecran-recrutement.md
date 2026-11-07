# Leçon 91 — L'écran Nouveau collègue

> **Commit** : `feat(desktop): l'écran Nouveau collègue`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/RecruitView.axaml(.cs)`, `MainWindow.axaml`, `Client.Core/ShellViewModel.cs`, `OfficeViewModel.cs`

## 1. Ce qu'on construit

« Recruter », dans l'en-tête de l'open space, ouvre l'écran **Nouveau collègue** :

- à gauche, un **badge d'employé** qui se balance doucement au bout de son cordon, avec le visage choisi, le prénom, le poste, le modèle et les outils du poste ; dessous, sa **première bulle** ;
- à droite, la **galerie** (les visages se soulèvent au survol, le choisi s'entoure de vert-de-gris), le **prénom** (avec un exemple en filigrane), le **poste** et les **traits**.

Une fois recruté (ou annulé), on revient à l'open space, qui **recharge son équipe** : le nouveau visage est au bureau.

## 2. Un style qui s'applique aussi aux dérivés

```xml
<Style Selector=":is(Button).chip">
```

En Avalonia, `Button.chip` ne vise **que** les `Button`. `:is(Button)` vise aussi les types dérivés, comme `ToggleButton` (les traits). Et l'état coché d'un `ToggleButton` a son propre style dans le thème : on le redéfinit (`ToggleButton.chip:checked`), sinon il redevient bleu.

## 3. Chaque clic répond

Survol, choix, cochage : chaque changement passe par une transition (`TransformOperationsTransition`, `BrushTransition`) de 200 ms. C'est la règle du projet, au bureau comme dans l'admin (leçon 79).

## 4. Quiz

1. Pourquoi `:is(Button)` plutôt que `Button` dans le sélecteur ?
2. Que fait l'open space au retour du recrutement ?

<details>
<summary>Réponses du quiz</summary>

1. Pour que le style s'applique aussi aux ToggleButton (les traits), qui dérivent de Button.
2. Il recharge l'équipe du projet : le nouveau collègue apparaît à son bureau, avec son visage et son prénom.

</details>
