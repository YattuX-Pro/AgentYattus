# Leçon 88 — On frappe à votre porte : la décision, sans raccourci

> **Commit** : `feat(desktop): on frappe à votre porte`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml`

## 1. Ce qu'on construit

Quand le rédacteur veut écrire, votre bureau vitré passe en **laiton** (transition de couleur), et une carte apparaît : « Malik frappe à votre porte ». « Voir et décider » ouvre la **relecture** : exactement ce qui sera écrit.

## 2. La règle de la leçon 26 tient toujours

« Approuver » reste **désactivé** tant que tout le contenu n'a pas été affiché (« Tout afficher »). L'open space ne fait que réutiliser `MissionViewModel` : la règle n'est pas recopiée, elle est la même, et le test de la leçon 78 la vérifie dans l'open space.

Une scène agréable ne doit jamais rendre la décision plus rapide que la lecture.

## 3. Quiz

1. Pourquoi ne pas mettre un bouton « Approuver » directement sur la carte de la porte ?

<details>
<summary>Réponses du quiz</summary>

1. Ce serait approuver sans avoir lu : la carte ouvre la relecture, où « Approuver » n'est possible qu'après avoir tout affiché.

</details>
