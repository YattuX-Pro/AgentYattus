# Leçon 102 — Le logiciel, au pixel près de la maquette

> **Commit** : `feat(desktop): les écrans au pixel près de la maquette, et le crayon « Modifier la fiche »`
> **Phase** : le bureau des agents
> **Fichiers** : `Assets/Fonts/*`, `Theme/Tokens.axaml`, `Theme/Controls.axaml`, `MainWindow.axaml`, `Views/*View.axaml`, `Views/Avatar.cs`, `Client.Core/{Agents,Home,Reports,Recruit,Office,Shell}ViewModel.cs`, `FaceBook.cs`, `MissionItem.cs`, tests

## 1. Ce qu'on construit

Une règle, maintenant obligatoire : **chaque écran du logiciel reproduit son artboard de la maquette « Bureau des agents »**. Mêmes polices, mêmes couleurs, mêmes tailles, mêmes espacements.

- **Les polices de la maquette** : Instrument Sans et IBM Plex Mono, embarquées (licence OFL).
- **La barre du haut** : 64 px, le logo de 30 px, des onglets sans icônes. Ton visage ouvre un menu avec l'espace admin et la déconnexion. La barre d'état disparaît.
- **Accueil** :
  - visages de 64 px, avec leur pastille d'état ;
  - un cercle « Recruter » en pointillés ;
  - les missions récentes en trois colonnes égales sur 1000 px.
- **Agents** :
  - une grille de trois colonnes ;
  - sur chaque carte, une pastille d'état (« En mission », « Attend votre accord », « Disponible ») et la mention PERSO pour un agent créé dans l'admin ;
  - une tuile « Recruter un collègue » ;
  - sur la fiche, les traits, puis le **crayon « Modifier la fiche »**.
- **Rapports** :
  - les missions en échec aussi, avec une bulle cinabre ;
  - le coût dans les chiffres ;
  - des dates du type « Aujourd'hui, 10:42 ».
- **Nouveau collègue** :
  - sa propre barre (retour, titre, équipe) ;
  - le badge au bout de son cordon ;
  - la galerie en neuf colonnes ;
  - le poste en sélecteur segmenté ;
  - les outils et le modèle du poste.
- **Open space** : la barre de l'artboard Main (la mission, l'équipe, « Recruter »), et la scène à 1016 × 804 px, à 24 px des bords.

## 2. Personnaliser un agent : le crayon

L'écran existait (« Nouveau collègue »), mais on ne l'atteignait que par un petit bouton de l'open space. La maquette le place sur la fiche de l'agent : le crayon ouvre « Nouveau collègue » **sur ce poste**, avec le visage, le prénom et les traits actuels déjà remplis. Enregistrer remplace le collègue : l'API désactive l'ancien titulaire du poste.

```csharp
var recruit = new RecruitViewModel(_api, project, card?.Agent.Key, card?.Colleague, ProjectName(project)) { BackTo = backTo };
```

## 3. Lire la maquette comme une spécification

Une maquette HTML donne des valeurs exactes. On les recopie, on ne les estime pas :

| Maquette (CSS) | Avalonia |
|---|---|
| `letter-spacing: -.02em` sur 22 px | `LetterSpacing="-0.44"` |
| `line-height: 1.45` sur 13 px | `LineHeight="18.85"` |
| `rgba(216,184,110,.45)` | `#73D8B86E` (alpha 0,45 × 255 = 0x73) |
| `grid-template-columns: repeat(3, 1fr); gap: 14px` | `UniformGrid Columns="3" ColumnSpacing="14" RowSpacing="14"` |
| `top: -8%` d'une image à 200 % | `Canvas.Top = -0.08 × taille` |

## 4. L'anneau, comme un box-shadow

La maquette dessine l'anneau d'un visage avec `box-shadow: 0 0 0 2px fond, 0 0 0 4px couleur`. Ce contour est **à l'extérieur** : il ne réduit pas le visage et ne décale pas la mise en page. `Avatar` fait pareil : il s'agrandit de l'anneau et compense par une marge négative. L'espace de 2 px prend la couleur de `Background`, pour que les visages qui se chevauchent restent détachés.

Piège : un contour qui déborde peut être coupé par son parent (bouton, liste). On laisse donc de la place autour (marge intérieure), ou on coupe la découpe (`ClipToBounds="False"`).

## 5. Ce qui reste

- **Nouveau collègue** : la consigne, les outils et le modèle s'affichent comme sur la maquette, mais en lecture seule. Ils viennent du poste, réglé dans l'admin, et l'API de recrutement ne les reçoit pas encore.
- **Open space** : il a sa propre barre, comme dans la maquette Main, donc pas d'onglets. Le logo ramène à l'accueil.
- **Comparaison** : elle a porté sur les valeurs de l'artboard et des rendus headless. Une vérification visuelle côte à côte avec la maquette ouverte reste à faire.

## 6. Quiz

1. Pourquoi `Avatar` reçoit-il une marge négative quand il a un anneau ?
2. Que vaut `rgba(94,158,146,.12)` en couleur Avalonia ?

<details>
<summary>Réponses du quiz</summary>

1. L'anneau est dessiné autour du visage, comme un box-shadow. Le contrôle grandit, et la marge négative garde sa place dans la mise en page.
2. `#1F5E9E92` : 0,12 × 255 ≈ 31 = 0x1F, suivi de la couleur 5E9E92.

</details>
