# Leçon 101 — Des visages partout où l'on parle d'un agent

> **Commit** : `feat(desktop): les visages de l'équipe sur l'accueil, les agents et les rapports`
> **Phase** : le bureau des agents
> **Fichiers** : `Client.Core/FaceBook.cs`, `MissionItem.cs`, `HomeViewModel.cs`, `AgentsViewModel.cs`, `ReportsViewModel.cs`, `ShellViewModel.cs`, `Desktop/Views/Avatar.cs`, `MainWindow.axaml`, `Views/HomeView.axaml`, `AgentsView.axaml`, `ReportsView.axaml`, tests

## 1. Ce qu'on construit

Les portraits faits avec Codex n'apparaissaient que dans l'open space et dans « Nouveau collègue ». On les met partout, d'après la maquette « Bureau des agents » :

- **Barre du haut** : quatre onglets (Accueil · Open space · Agents · Rapports), et votre visage à droite.
- **Accueil** : l'équipe en vrais visages. **Un clic sur un visage choisit qui fera la mission**, et le titre devient « Quelle mission pour Inès ? ». Le choix de l'agent montre son visage ; avec REPORT.md, on voit Inès puis Malik. Chaque mission récente montre qui y travaille, avec un halo vert-de-gris quand il travaille et laiton quand « Malik frappe à votre porte ».
- **Agents** : une carte par agent avec la photo du collègue, ses missions, sa réussite et son coût. La fiche le montre en pose « parle », avec une bulle où il se présente, et un bouton « Confier une mission à Inès » qui ramène à l'accueil avec Inès choisie.
- **Rapports** : racontés comme une conversation. Votre question à droite, la réponse de l'agent à gauche, et la pastille REPORT.md quand il a écrit avec votre accord.

## 2. Un seul endroit pour les visages

Trois écrans ont besoin des mêmes visages. Plutôt que de répéter les appels, `FaceBook` les garde en mémoire : une équipe par projet, une image par portrait et par pose, demandées **une seule fois**.

```csharp
public async Task<Face?> AgentAsync(Guid? project, string role)
{
    var colleague = (await TeamAsync(project))?.Colleagues.FirstOrDefault(c => c.Active && c.Role == role);
    return colleague is null ? null : await FaceAsync(colleague.Name, colleague.PortraitId);
}
```

On met en cache la **tâche** et non le résultat : deux écrans qui demandent la même image au même moment attendent la même requête.

Un siège libre n'a pas de photo, seulement un nom : `Avatar` affiche alors l'initiale. Un visage manquant ne bloque jamais un écran.

## 3. Des phrases avec des prénoms

`MissionItem.Line` ne dit plus « À valider » mais **« Malik frappe à votre porte »**. Ce n'est pas qu'un détail de texte : le statut devient quelqu'un. C'est toute l'idée du bureau des agents, de collègues plutôt que de processus.

## 4. Un contrôle `Avatar`, sans XAML

Le portrait (560 × 560, la tête en haut au centre) est zoomé ×2 sur le visage, dans un rond. L'anneau, optionnel, change de couleur en fondu. Comme tout se calcule à partir de la taille, `Avatar` est écrit en C# : une `Border` qui règle elle-même le zoom, le cadrage et l'anneau quand `Size` ou `Ring` changent.

## 5. Ce qui reste

- Les chiffres des agents sont réservés aux administrateurs (`llm-admin`). Pour les autres, les cartes affichent « — ».
- Léon dirige l'équipe mais ne prend pas encore de mission seul : son visage est sur l'accueil, il n'est pas proposé dans le choix de l'agent.

## 6. Quiz

1. Pourquoi `FaceBook` met-il en cache la tâche plutôt que le résultat ?
2. Que voit-on quand un agent n'a pas de collègue à son poste ?

<details>
<summary>Réponses du quiz</summary>

1. Deux écrans qui demandent la même image au même moment attendent la même requête, au lieu d'en lancer deux.
2. Son nom (le nom de l'agent, ou son identifiant rendu lisible) et son initiale dans un rond : pas de photo, mais rien ne casse.

</details>
