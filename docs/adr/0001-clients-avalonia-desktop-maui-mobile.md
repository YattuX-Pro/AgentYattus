# ADR 0001 — Clients : Avalonia pour le desktop, .NET MAUI pour le mobile

- **Statut** : accepté
- **Date** : 2026-10-29

## Contexte

Agent Yattus a deux clients :

- un **centre de contrôle desktop**, le client du MVP : saisie de mission, graphe des agents en direct, carte d'approbation, frise chronologique ;
- un **compagnon mobile**, prévu après le MVP : suivre une mission, approuver ou refuser une action.

Le design est sur mesure (thème sombre « observatoire », orbes, graphe de nœuds) et doit avoir le même rendu sur toutes les machines. Le projet est open source : ses contributeurs peuvent travailler sous Windows, macOS ou Linux.

## Décision

- **Desktop : Avalonia 12** (version 12.1.4 au 2026-10-29, licence MIT, cible `net10.0`).
- **Mobile : .NET MAUI**, dans la version supportée au moment où on le commencera. MAUI 10 n'est plus supporté après le 11 mai 2027.
- **Code partagé** : tout ce qui n'est pas de l'interface vit dans des bibliothèques `net10.0` qui ne référencent ni Avalonia ni MAUI : les DTO de `Contracts`, le client HTTP et SignalR, les ViewModels réutilisables (CommunityToolkit.Mvvm). Seules les vues sont écrites deux fois.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| MAUI partout (plan initial) | Une seule pile d'interface | Sur macOS, le desktop passe par Mac Catalyst ; pas de support Linux ; les contrôles sont natifs, donc un design sur mesure est plus difficile à rendre à l'identique |
| Avalonia partout | Un seul dialecte XAML | Le paquet NuGet présente encore le mobile comme expérimental ; MAUI est la pile mobile officielle de Microsoft |
| **Avalonia + MAUI** | Chaque outil est utilisé sur son point fort | Deux dialectes XAML, deux jeux de vues et de styles |

## Conséquences

- (+) Le desktop est natif sur Windows, macOS et Linux, avec le même rendu partout : Avalonia dessine ses propres contrôles.
- (+) Le MVP n'a qu'un seul client à construire ; le mobile vient après.
- (−) Il faut apprendre deux syntaxes XAML, et déclarer deux fois les styles du design (couleurs, polices).
- (−) Une règle à tenir : aucun ViewModel partagé ne référence Avalonia ou MAUI. C'est la structure des projets qui la garantit (une bibliothèque partagée sans dépendance d'interface), et non la discipline.
- Rien n'est créé tout de suite : ces projets naîtront quand on commencera le client desktop, après les spikes.

## Sources

- Avalonia 12.1.4 sur NuGet : <https://www.nuget.org/packages/Avalonia/12.1.4>
- Plateformes supportées par .NET MAUI : <https://learn.microsoft.com/dotnet/maui/supported-platforms>
- Politique de support de .NET MAUI : <https://dotnet.microsoft.com/platform/support/policy/maui>
