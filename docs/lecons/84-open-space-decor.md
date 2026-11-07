# Leçon 84 — L'open space remplace l'écran Mission : un plan fixe dans un Viewbox

> **Commit** : `feat(desktop): l'open space remplace l'écran Mission`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml(.cs)`, `MainWindow.axaml`, `Client.Core/ShellViewModel.cs`, `OfficeViewModel.cs`, `Converters/BytesToBitmapConverter.cs`

## 1. Ce qu'on construit

Ouvrir une mission affiche désormais l'**open space** de son projet : le mur et ses fenêtres, l'armoire des archives (le workspace), une plante, votre bureau vitré. L'ancien écran (le graphe) est supprimé ; sa logique de décision reste dans `MissionViewModel`, réutilisé.

## 2. Un plan fixe, une fenêtre libre

La scène est dessinée sur un `Canvas` de **1016 × 804**, placé dans un `Viewbox` : quelle que soit la taille de la fenêtre, tout s'agrandit ou se réduit d'un bloc. C'est ce qui permet au ViewModel de calculer les trajets en coordonnées fixes (leçon 78).

## 3. Battre la mesure

```csharp
_clock.Tick += (_, _) => { var dt = _elapsed.Elapsed; _elapsed.Restart(); office.Tick(dt); };
```

Un `DispatcherTimer` de 16 ms (60 images par seconde) dans la vue, qui passe le temps **réellement** écoulé (un `Stopwatch`) : si une image prend du retard, l'animation ne ralentit pas. Le minuteur s'arrête quand la vue quitte l'écran.

## 4. Des octets, pas des images

Client.Core ne connaît pas Avalonia : il expose les portraits en `byte[]`. Le convertisseur les transforme en `Bitmap` dans la vue, avec un petit cache, puisque les mêmes visages reviennent à chaque réplique.

## 5. Quiz

1. Pourquoi un Viewbox autour d'un plan de taille fixe ?
2. Pourquoi passer le temps écoulé mesuré plutôt que « 16 ms » à chaque tic ?

<details>
<summary>Réponses du quiz</summary>

1. Les trajets et positions se calculent une fois, en coordonnées fixes ; le Viewbox adapte tout à la fenêtre.
2. Un tic peut arriver en retard ; avec le temps réel, l'animation garde sa vitesse au lieu de ralentir.

</details>
