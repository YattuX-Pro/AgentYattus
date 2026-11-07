# Leçon 78 — L'open space sans minuteur : le temps comme paramètre

> **Commit** : `feat(client): OfficeViewModel, la scène qui se joue`
> **Phase** : le bureau des agents
> **Fichiers** : `Client.Core/OfficeViewModel.cs`, `MissionViewModel.cs`, `tests/…/OfficeViewModelTests.cs`

## 1. Ce qu'on construit

`OfficeViewModel` tient les quatre bureaux (chef, analyste, rédacteur, vous), le fil de l'équipe, l'objet qui voyage et la demande d'accord. La vue appelle `Tick(temps écoulé)` à chaque image ; le ViewModel fait avancer la bulle lettre par lettre, l'objet sur sa courbe, puis la réplique entre dans le fil.

## 2. Pas de minuteur dans le ViewModel

```csharp
office.Tick(TimeSpan.FromMilliseconds(50));
Assert.Equal("Je li", office.Analyst.Bubble); // 10 ms par lettre
```

Un `DispatcherTimer` dans le ViewModel attacherait le test à Avalonia et au temps réel. Ici le temps est un **paramètre** : le test avance l'horloge à la main, et la vitesse vient des réglages de l'admin (`TypingMs`, `PauseMs`).

## 3. Ouvrir une mission terminée

Au premier chargement, tout ce qui s'est déjà passé entre **directement** dans le fil ; seule la dernière réplique se joue. On ne fait pas attendre deux minutes pour revoir une mission finie.

## 4. La décision ne change pas

L'open space réutilise `MissionViewModel` pour la décision. La règle de la leçon 26 tient toujours : « Approuver » reste désactivé tant que tout le contenu à écrire n'a pas été affiché. Un test le vérifie dans l'open space même.

## 5. Une courbe douce

L'objet suit une courbe de Bézier cubique, avec une accélération douce au départ et à l'arrivée (*ease-in-out*). Les points de passage sont le plan de l'open space, en unités de la vue : c'est de la mise en page, pas du contenu.

## 6. Quiz

1. Pourquoi passer le temps écoulé à `Tick` plutôt que d'utiliser un minuteur ?
2. Pourquoi l'open space réutilise-t-il `MissionViewModel` pour la décision ?

<details>
<summary>Réponses du quiz</summary>

1. Pour tester sans attendre ni dépendre de l'interface : le test choisit le temps qui passe.
2. Pour garder une seule règle de décision (tout relire avant d'approuver), déjà testée, au lieu d'en écrire une seconde.

</details>
