# Leçon 100 — Un système de design pour le logiciel : plus aucun contrôle « natif »

> **Commit** : `feat(desktop): boutons, champs et listes dessinés, chaque clic s'anime`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Theme/Controls.axaml`, `Views/LoginView.axaml`, `Views/RecruitView.axaml`, `Views/OfficeView.axaml`, `Program.cs`

## 1. Ce qu'on construit

La règle de l'admin web s'applique maintenant au logiciel : **chaque clic s'anime**, et aucun contrôle ne garde le dessin par défaut de Fluent.

- **Boutons** : six familles (`primary`, `gold`, `outline`, `chip`, `send`, `tab`…). Chacune a tous ses états : repos, survol, **appui**, désactivé. Sous le doigt, le bouton s'enfonce (`scale(0.96)`) et ses couleurs changent en fondu.
- **Champs** : un fond graphite, un filet qui s'éclaire au survol, puis un anneau vert-de-gris au focus.
- **Listes déroulantes** : une pilule sombre. La flèche tourne quand la liste s'ouvre, et le menu est une carte arrondie dont les lignes s'allument au survol.
- **Infobulles, barres de défilement, listes** : aux couleurs du logiciel.

## 2. Pourquoi c'était « natif »

Chaque écran définissait la couleur de ses boutons **au repos** et **au survol**, mais pas **à l'appui**. Au clic, Fluent reprenait donc la main et affichait son gris ou son bleu pendant une fraction de seconde. Les listes déroulantes et les champs, eux, n'avaient aucun style. La correction : un seul fichier, `Theme/Controls.axaml`, où chaque classe décrit tous ses états. Les écrans ne gardent que ce qui leur est propre (tailles, cartes).

```xml
<Style Selector="Button.primary:pressed /template/ ContentPresenter#PART_ContentPresenter">
  <Setter Property="Background" Value="#548D83" />
</Style>
```

## 3. Le piège d'Avalonia 12

Un style comme `TextBox /template/ Border#PART_BorderElement`, sans classe ni pseudo-classe, **ne s'applique pas** aux parties des contrôles Fluent. La sonde headless le montre : la valeur garde la priorité `Template`, celle du thème. Ajouter un déclencheur suffit, par exemple `:not(.composer)` :

```xml
<Style Selector="TextBox:not(.composer) /template/ Border#PART_BorderElement">
```

Le style passe alors en priorité `StyleTrigger` et l'emporte. Bonus : `:not(.composer)` laisse au compositeur de l'accueil son champ nu.

**Leçon de méthode** : quand un style ne prend pas, on ne devine pas. On mesure : la sonde affiche la valeur et sa priorité (`GetDiagnostic`).

## 4. Le mouvement

- **Changer d'écran** : `TransitioningContentControl` avec un fondu enchaîné. L'écran qui arrive monte en plus de 12 px (style `.screen`).
- **Les cartes et lignes de liste** entrent en glissant (`.enter`). Les cartes cliquables se soulèvent au survol (`.lift`).
- **Les couleurs** changent en fondu (`BrushTransition`), jamais d'un coup.

Et le logiciel parle français jusque dans ses dates : la culture est fixée à `fr-FR` au démarrage.

## 5. Quiz

1. Pourquoi un bouton bien stylé au repos et au survol pouvait-il encore « faire Fluent » ?
2. Pourquoi ajouter `:not(.composer)` à un style suffit-il à le faire appliquer ?

<details>
<summary>Réponses du quiz</summary>

1. L'état `:pressed` n'était pas défini : au clic, le thème Fluent reprenait la main avec ses couleurs.
2. Avec une pseudo-classe, le style devient un déclencheur (priorité `StyleTrigger`), qui passe devant les valeurs du thème. Sans déclencheur, en Avalonia 12, il ne s'appliquait pas aux parties du gabarit.

</details>
