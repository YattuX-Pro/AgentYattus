# Leçon 76 — Des événements qui disent qui fait quoi

> **Commit** : `feat(missions): le rôle et le fichier visé dans le journal`
> **Phase** : le bureau des agents
> **Fichiers** : `Agents/AgentFrameworkRunner.cs`, `Admin/src/app/core/events.ts`, `pages/missions/missions.ts`, tests

## 1. Le problème

Le journal disait `outil:read_file`. Pour que le bureau fasse dire « Je lis OrderService.cs » **par la bonne personne**, il faut savoir **quel rôle** a appelé l'outil, et **sur quel fichier**.

## 2. Un événement de plus, l'ancien reste

```
outil:read_file
action:analyste:read_file:src/Orders/OrderService.cs
```

On garde `outil:…` tel quel : les statistiques, le filtre de l'audit et le décompte des outils du bureau s'en servent. On **ajoute** `action:…`. Changer le format de l'ancien aurait cassé trois écrans pour rien.

Dans l'admin, le déroulé d'une mission montre la ligne la plus précise et cache la ligne `outil:` redondante. Une mission ancienne, qui n'a pas d'`action:`, s'affiche comme avant.

## 3. Ce qu'on écrit, et ce qu'on n'écrit pas

- **Le chemin seulement.** Pour `write_file`, le contenu reste dans la mission ; le journal ne le reçoit jamais.
- **Nettoyé** : les caractères de contrôle et invisibles sont retirés. Un retour à la ligne dans un chemin ne peut pas fabriquer une fausse ligne de journal.
- **Borné** : 120 caractères au plus, en gardant la fin (le nom du fichier est la partie utile).

## 4. Quiz

1. Pourquoi ajouter `action:` plutôt que modifier `outil:` ?
2. Pourquoi retirer les retours à la ligne d'un chemin avant de l'écrire dans le journal ?

<details>
<summary>Réponses du quiz</summary>

1. Plusieurs écrans comptent ou filtrent `outil:` ; un nouvel événement ne casse rien.
2. Pour qu'un nom de fichier fabriqué ne puisse pas faire croire à un autre événement (injection dans les journaux).

</details>
