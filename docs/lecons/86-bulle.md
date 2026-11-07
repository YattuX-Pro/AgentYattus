# Leçon 86 — La bulle qui s'écrit

> **Commit** : `feat(desktop): la bulle en forme de nuage`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml`

## 1. Ce qu'on construit

Au-dessus de la tête de celui qui parle, une bulle en forme de **nuage** (une douzaine d'ellipses de la même couleur, et deux petites bulles vers la tête) apparaît en grossissant, avec un léger rebond. Le texte s'y écrit lettre par lettre (`Bubble`, fourni par le ViewModel), et un curseur clignote.

## 2. Une transition avec un rebond

```xml
<TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.35" Easing="BackEaseOut" />
```

`BackEaseOut` dépasse un peu la taille finale puis revient : la bulle « éclot ». L'origine de la transformation est en bas à gauche (`20%,100%`), là où sont les petites bulles : le nuage sort de la tête.

## 3. Quiz

1. Pourquoi l'origine de la transformation est-elle en bas à gauche ?
2. Qui décide du texte affiché à chaque instant ?

<details>
<summary>Réponses du quiz</summary>

1. Pour que le nuage grandisse depuis la tête du collègue, comme une pensée qui sort.
2. Le ViewModel (Bubble, lettre par lettre, selon la vitesse de l'admin) ; la vue ne fait qu'afficher.

</details>
