# Leçon 77 — Traduire une mission en scène : une fonction pure

> **Commit** : `feat(client): traduire le journal d'une mission en répliques`
> **Phase** : le bureau des agents
> **Fichiers** : `Client.Core/Scene.cs`, `ApiClient.cs`, `MissionItem.cs`, `tests/…/SceneTests.cs`

## 1. Ce qu'on construit

`Scene.From(journal, équipe, objectif, vous)` renvoie la liste des répliques : **qui parle**, **ce qu'il dit**, **ce qu'il fait passer** (dossier, constats, brouillon) et **ce que fait chacun** ensuite.

| Événement du journal | Qui parle | Réplique (réglée dans l'admin) | Objet |
|---|---|---|---|
| `Running` (le premier) | chef | « On démarre : {objectif} » | |
| `action:analyste:read_file:src/Api.cs` | analyste | « Je lis {fichier}. » | dossier (la 1re fois) |
| `agent:redacteur` | analyste | « {redacteur}, je t'envoie mes constats. » | constats |
| `approbation-demandee:write_file` | rédacteur | « Je peux écrire {fichier} ? » | brouillon |
| `Completed` | chef | « Mission terminée… » | |

## 2. Pourquoi une fonction pure

Pas de réseau, pas d'horloge, pas d'interface : un journal entre, des répliques sortent. Le test donne un journal écrit à la main et vérifie toute la conversation, en quelques millisecondes. L'animation (leçon 78) n'a plus qu'à jouer la liste.

## 3. Le comportement ici, le contenu ailleurs

La fonction décide **qui** parle et **quoi** circule ; le **texte** vient de l'API (les répliques de l'admin). Un événement sans réplique est simplement muet : une équipe sans réplique « mission:echec » ne plante pas, elle se tait.

`{variable}` est remplacée si elle est connue ; une variable inconnue reste visible telle quelle. L'admin refuse déjà les variables inconnues (leçon 73) : c'est une seconde barrière, qui ne cache rien.

## 4. Quiz

1. Pourquoi « Running » ne fait-il parler le chef qu'une fois ?
2. Qu'arrive-t-il à un événement qui n'a pas de réplique ?

<details>
<summary>Réponses du quiz</summary>

1. Une mission reprise après une décision repasse par « Running » : le chef ne réannonce pas le départ.
2. Il est muet : pas de bulle, pas d'erreur.

</details>
