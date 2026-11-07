# Leçon 89 — Le fil de l'équipe

> **Commit** : `feat(desktop): le fil de l'équipe`
> **Phase** : le bureau des agents
> **Fichiers** : `Desktop/Views/OfficeView.axaml(.cs)`

## 1. Ce qu'on construit

À droite de l'open space, l'historique des répliques comme une messagerie d'entreprise : le visage (recadré dans un cercle), le prénom, l'heure, le texte, et le fichier joint quand un objet a circulé. En bas, « Malik écrit… » pendant qu'une bulle s'écrit.

## 2. Une entrée animée, un défilement automatique

Chaque message entre en fondu avec un glissement de 8 px (une `Animation` jouée à l'apparition). La vue écoute `Feed.CollectionChanged` et descend jusqu'au dernier message ; elle le fait après le rendu (`Dispatcher.UIThread.Post`, priorité basse), sinon la hauteur du nouveau message n'est pas encore connue.

## 3. Quiz

1. Pourquoi faire défiler après le rendu plutôt qu'immédiatement ?

<details>
<summary>Réponses du quiz</summary>

1. Le nouveau message n'a pas encore de taille : défiler tout de suite s'arrêterait juste avant lui.

</details>
