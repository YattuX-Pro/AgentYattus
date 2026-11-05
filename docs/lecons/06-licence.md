# Leçon 06 — Choisir une licence

> **Commit** : `docs: ADR 0002, licence du projet`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `docs/adr/0002-licence.md`, `LICENSE`

## 1. Ce qu'on construit

Le fichier `LICENSE`, qui dit **ce que les autres ont le droit de faire** avec le code, et l'ADR qui explique ce choix.

## 2. Le problème

On croit souvent qu'un dépôt public est « libre ». C'est faux : **sans licence, personne n'a le droit de réutiliser le code**. Le droit d'auteur s'applique par défaut. Un dépôt public sans licence, c'est une vitrine : on peut regarder, pas emporter.

## 3. Les trois grandes familles

| Licence | En une phrase | Exemple de projets |
|---|---|---|
| **MIT** | « Fais ce que tu veux, garde juste mon nom et ce texte. » | .NET, Avalonia, Agent Framework |
| **Apache 2.0** | Comme MIT, avec en plus une protection sur les **brevets** et l'obligation de signaler les fichiers modifiés. | Kubernetes, Android |
| **AGPL 3.0** | « Si tu modifies et que tu fais tourner ça comme service en ligne, publie ton code. » | Certains outils auto-hébergés |

- MIT et Apache sont dites **permissives** : on peut les réutiliser presque sans contrainte, même dans un produit commercial fermé.
- L'AGPL est dite **à réciprocité forte** (*copyleft*) : ce qui en dérive doit rester ouvert.

## 4. Le choix : MIT

- Le projet veut être **réutilisé**, y compris en entreprise.
- MIT est **la licence de l'écosystème .NET** : nos dépendances principales l'utilisent.
- Elle est **courte et connue** : un contributeur la comprend en 30 secondes.

**Ce qu'on accepte en échange :**

- quelqu'un peut bâtir un service fermé sur le projet sans rien rendre ;
- MIT ne parle pas des brevets. Si de grandes entreprises contribuent un jour, on pourra réévaluer Apache 2.0.

L'ADR a le statut **« proposé »**. C'est à toi de le passer à « accepté » avant de rendre le dépôt public : c'est une décision juridique qui t'engage.

## 5. Une conséquence pratique

Chaque nouvelle dépendance doit avoir une **licence compatible**. C'est pourquoi VoiceStudio (AGPL, modèles non commerciaux), étudié le 29 octobre, ne sera jamais copié dans ce dépôt. S'il est utilisé un jour, ce sera comme programme séparé.

## 6. Vérifie toi-même

Ouvre `LICENSE`. Repère la seule condition imposée aux utilisateurs : elle tient en une phrase.

## 7. Quiz

1. Un dépôt public sans licence est-il libre de droits ?
2. Quelle différence principale entre MIT et AGPL ?
3. Pourquoi faut-il vérifier la licence de chaque dépendance ?

## 8. Pour aller plus loin

- Choose a License : <https://choosealicense.com/licenses/>
- Texte de la licence MIT : <https://opensource.org/license/mit>

<details>
<summary>Réponses du quiz</summary>

1. Non. Sans licence, le droit d'auteur s'applique : personne n'a le droit de réutiliser le code.
2. MIT autorise presque tout, même un usage commercial fermé. L'AGPL oblige à publier le code de toute version modifiée, même utilisée seulement comme service en ligne.
3. Parce qu'une dépendance sous une licence incompatible peut imposer ses conditions au projet entier.

</details>
