# Leçon 02 — Première décision : Avalonia pour le desktop, MAUI pour le mobile

> **Commit** : `docs: ADR 0001, clients Avalonia et MAUI`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `docs/adr/0001-clients-avalonia-desktop-maui-mobile.md`

## 1. Ce qu'on construit

Notre premier **ADR** (*Architecture Decision Record*, fiche de décision d'architecture). Il répond à la question : **avec quelle technologie construit-on les applications que l'utilisateur voit ?**

Un ADR est un court document, toujours dans le même ordre :

1. **Contexte** : la situation et les contraintes ;
2. **Décision** : ce qu'on choisit ;
3. **Alternatives** : ce qu'on a écarté, et pourquoi ;
4. **Conséquences** : les avantages, et surtout les inconvénients qu'on accepte.

> **Pourquoi l'écrire ?** Dans six mois, quelqu'un demandera « pourquoi deux technologies d'interface ? ». Sans ADR, plus personne ne s'en souvient, et on risque de refaire le débat, ou de défaire une bonne décision.

## 2. Le problème

Agent Yattus a deux interfaces :

- un **centre de contrôle desktop** (Windows, macOS, Linux), le client du MVP ;
- un **compagnon mobile** (Android, iOS), pour suivre et approuver à distance.

Le design est **sur mesure** : thème sombre, orbes, graphe d'agents. Il doit avoir **le même rendu partout**.

## 3. Les options

| Option | Pour | Contre |
|---|---|---|
| .NET MAUI partout | Une seule technologie | Pas de Linux ; sur macOS le desktop passe par Mac Catalyst ; les contrôles natifs rendent un design sur mesure difficile à reproduire à l'identique |
| Avalonia partout | Un seul langage d'interface | Le mobile y est encore présenté comme expérimental |
| **Avalonia (desktop) + MAUI (mobile)** | Chaque outil sur son point fort | Deux syntaxes XAML à apprendre, les styles écrits deux fois |

## 4. Le choix

- **Avalonia 12** pour le desktop. Avalonia **dessine lui-même** ses contrôles : le rendu est identique sur Windows, macOS et Linux.
- **.NET MAUI** pour le mobile : c'est la technologie mobile officielle de Microsoft.
- **Tout ce qui n'est pas de l'interface est partagé** : les objets échangés avec l'API, le client HTTP, les ViewModels. Seules les vues sont écrites deux fois.

### Vocabulaire

| Mot | Sens |
|---|---|
| **XAML** | Le langage, proche du HTML, qui décrit une interface en .NET. |
| **ViewModel** | La classe qui contient les données et les actions d'un écran, sans rien savoir de son apparence. |
| **Natif** | Qui utilise les composants du système (les boutons d'iOS, par exemple). |

## 5. La règle qui découle de ce choix

> Un ViewModel partagé ne référence **jamais** Avalonia ni MAUI.

Cette règle n'est pas laissée à la discipline : les ViewModels vivent dans une bibliothèque qui **ne connaît pas** ces technologies. Si quelqu'un essaie d'y utiliser Avalonia, le code ne compile pas.

## 6. Vérifie toi-même

Ouvre l'ADR et retrouve les deux conséquences négatives (−) que le projet accepte.

## 7. Quiz

1. Quelles sont les quatre parties d'un ADR ?
2. Pourquoi Avalonia plutôt que MAUI pour le desktop ?
3. Qu'est-ce qui est partagé entre le desktop et le mobile ?

## 8. Pour aller plus loin

- Architecture Decision Records : <https://adr.github.io>
- Avalonia : <https://docs.avaloniaui.net>
- .NET MAUI : <https://learn.microsoft.com/dotnet/maui>

<details>
<summary>Réponses du quiz</summary>

1. Contexte, Décision, Alternatives, Conséquences.
2. Parce qu'Avalonia dessine ses propres contrôles : même rendu sur Windows, macOS et Linux, ce qui convient à un design sur mesure. MAUI ne gère pas Linux.
3. Tout ce qui n'est pas de l'interface : les objets échangés avec l'API, le client HTTP et SignalR, les ViewModels.

</details>
