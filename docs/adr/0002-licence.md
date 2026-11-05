# ADR 0002 — Licence : MIT

- **Statut** : proposé
- **Date** : 2026-11-05

## Contexte

Agent Yattus sera publié en open source. Sans licence, un dépôt public n'accorde aucun droit : personne ne peut légalement réutiliser, modifier ou redistribuer le code. Il faut donc en choisir une avant de publier.

Ce que le projet veut :

- que n'importe qui puisse lire, essayer et réutiliser le code, y compris dans un cadre professionnel ;
- rester dans les usages de l'écosystème .NET ;
- ne pas imposer de démarches lourdes aux contributeurs.

Les principales dépendances sont sous licence MIT : Avalonia, CommunityToolkit.Mvvm, Microsoft Agent Framework (`Microsoft.Agents.AI`), et .NET lui-même.

## Décision

**Licence MIT**, avec le fichier `LICENSE` à la racine du dépôt.

## Alternatives envisagées

| Licence | Ce qu'elle impose | Pour | Contre |
|---|---|---|---|
| **MIT** | Garder la mention de copyright et le texte de la licence | Très courte, très connue, la même que l'écosystème .NET | Pas de clause explicite sur les brevets |
| Apache 2.0 | MIT + signaler les fichiers modifiés + clause sur les brevets | Protège mieux contre les litiges de brevets | Plus longue, un peu plus de formalités pour les contributeurs |
| AGPL 3.0 | Publier le code source de toute version modifiée, même utilisée seulement à travers un réseau | Empêche qu'un service fermé soit bâti sur le projet sans rien rendre | Freine fortement l'adoption, en particulier en entreprise |

## Conséquences

- (+) N'importe qui peut réutiliser le code, y compris dans un produit commercial fermé, à condition de garder la mention de copyright.
- (+) Aucune incompatibilité avec les dépendances actuelles, qui sont elles aussi sous licences permissives.
- (−) Quelqu'un peut construire un service fermé à partir du projet sans rien publier en retour.
- (−) Pas de protection explicite contre les brevets. Si le projet attire des contributions d'entreprises, il faudra réévaluer Apache 2.0.
- Chaque nouvelle dépendance doit avoir une licence compatible avec MIT. C'est la raison pour laquelle VoiceStudio (AGPL, modèles non commerciaux) ne sera jamais copié dans ce dépôt.

## Sources

- Choose a License : <https://choosealicense.com/licenses/>
- Texte de la licence MIT : <https://opensource.org/license/mit>
