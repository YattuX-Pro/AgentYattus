# Leçon 85 — Les collègues à leur bureau : deux poses et un fondu

> **Commit** : `feat(desktop): les collègues à leur bureau`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml`

## 1. Ce qu'on construit

Un `DataTemplate` « bureau » sert aux trois collègues : les deux poses superposées, le meuble, la plaque (prénom, rôle) et le statut. Un siège sans collègue affiche « Siège libre » en pointillé.

## 2. Une classe, deux styles, une transition

```xml
<Panel Classes.speaking="{Binding IsSpeaking}">
<Style Selector="Panel.speaking Image.talk"><Setter Property="Opacity" Value="1" /></Style>
```

Le ViewModel dit seulement « il parle ». La vue en déduit les deux opacités, et une `DoubleTransition` de 250 ms fait le fondu. Aucun code d'animation dans le ViewModel.

## 3. Celui qui parle passe devant

Les bureaux se chevauchent (la bulle d'un collègue déborde sur le voisin). `ZIndex="{Binding Leader.Layer}"` met celui qui parle au premier plan. Et `ClipToBounds="False"` sur le conteneur : sinon, la bulle serait coupée au bord du bureau.

## 4. Quiz

1. Où vit la décision « afficher la pose parle » ?
2. Pourquoi changer l'ordre d'affichage de celui qui parle ?

<details>
<summary>Réponses du quiz</summary>

1. Dans la vue : le ViewModel expose IsSpeaking, un style en déduit les opacités.
2. Sa bulle déborde sur le bureau voisin ; dessinée dessous, elle serait cachée.

</details>
