# Leçon 87 — Ce qui circule entre les bureaux

> **Commit** : `feat(desktop): les dossiers qui passent d'un bureau à l'autre`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml`

## 1. Ce qu'on construit

Un dossier vert-de-gris, avec son étiquette (le fichier, « constats » ou « REPORT.md »), suit une courbe des archives vers l'analyste, puis de l'analyste vers le rédacteur, puis du rédacteur vers votre bureau.

## 2. La position vient du ViewModel

```xml
<StackPanel Canvas.Left="{Binding TokenX}" Canvas.Top="{Binding TokenY}" IsVisible="{Binding IsCarrying}" … />
```

`TokenX` et `TokenY` sont recalculés à chaque `Tick` sur une courbe de Bézier avec accélération douce (leçon 78). La vue se contente de les lier : l'animation est testée sans Avalonia.

## 3. Quiz

1. Pourquoi la position de l'objet est-elle calculée par le ViewModel plutôt que par une animation XAML ?

<details>
<summary>Réponses du quiz</summary>

1. Le trajet dépend de la réplique en cours (quel objet, quand) : le ViewModel le sait et le teste ; la vue ne fait que lier deux nombres.

</details>
