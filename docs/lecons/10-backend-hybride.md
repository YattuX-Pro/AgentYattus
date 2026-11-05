# Leçon 10 — Un backend hybride, local ou serveur

> **Commit** : `docs: ADR 0004, backend hybride local/serveur`
> **Phase** : 1, Agent Framework
> **Fichiers** : `docs/adr/0004-backend-hybride.md`

## 1. Ce qu'on construit

Encore une décision, pas de code : **où tourne le backend ?** Sur l'ordinateur de l'utilisateur, ou sur un serveur ? La réponse est : **les deux, avec le même programme**.

> **Une image** : un même moteur de voiture peut équiper une citadine ou une camionnette. On change la carrosserie et les réglages, pas le moteur. Ici, le moteur, c'est l'API. La carrosserie, c'est la base de données, l'adresse d'écoute et la sécurité.

## 2. Le problème

Deux usages s'opposent :

- **Seul sur son ordinateur** : on veut installer le desktop et lancer des missions sur ses dossiers. Sans louer de serveur, sans installer Docker.
- **En équipe ou en mobilité** : on veut partager les missions, et approuver une action depuis son téléphone, hors de chez soi.

Un serveur obligatoire gênerait le premier usage. Un fonctionnement purement local rendrait le second impossible.

## 3. Les options

| Option | Le souci |
|---|---|
| Serveur seulement | Il faut un serveur même pour un usage solo, et rien ne marche hors ligne. |
| Local seulement | Pas de partage, pas de téléphone à distance. |
| Deux backends différents | Tout coder deux fois. |
| Le backend à l'intérieur du desktop | Le téléphone ne peut pas s'y connecter, et un plantage de l'interface arrêterait les missions. |
| **Le même programme, deux modes** | Le desktop doit lancer et arrêter l'API ; il faut deux séries de migrations. |

## 4. Le choix : un programme, deux modes

| | Mode local | Mode serveur |
|---|---|---|
| **Base de données** | SQLite : un simple fichier sur l'ordinateur | PostgreSQL |
| **Qui lance l'API ?** | Le desktop, en *sidecar* | L'hébergeur, dans un conteneur Docker |
| **Qui peut la joindre ?** | Seulement l'ordinateur lui-même (`127.0.0.1`) | Internet, en HTTPS |
| **Protection** | Un jeton secret, nouveau à chaque lancement | Des comptes utilisateurs (ADR à venir) |
| **Clé du modèle IA** | Celle de l'utilisateur, dans le coffre du système | Dans les secrets du serveur |

### Quelques mots expliqués

- **SQLite** : une base de données qui tient dans **un seul fichier**, sans serveur à installer. Parfait pour une personne seule.
- **PostgreSQL** : une vraie base serveur, faite pour beaucoup d'utilisateurs en même temps.
- **Sidecar** (« side-car » d'une moto) : un programme lancé **à côté** de l'application principale, qui vit et meurt avec elle. Le desktop démarre l'API en arrière-plan, et l'arrête en se fermant.
- **`127.0.0.1`** : l'adresse qui veut dire « cette machine-ci ». Un programme qui n'écoute que là est invisible depuis le réseau.
- **Jeton par lancement** : même sur `127.0.0.1`, **n'importe quel autre programme de l'ordinateur** pourrait appeler l'API. Le desktop génère donc un secret au démarrage, et l'API refuse toute requête qui ne le présente pas.

### Ce que les clients savent

Rien de tout cela. Le desktop et le téléphone ne connaissent **qu'une adresse**, par exemple `http://127.0.0.1:5080/` ou `https://yattus.mon-domaine.fr/`. C'est déjà le cas aujourd'hui : le desktop lit la variable `AGENTYATTUS_API_URL`.

## 5. Les conséquences

**Avantages**

- Un seul code backend à maintenir.
- Il y a déjà une preuve que ça peut marcher : **les tests de l'API tournent sur SQLite** avec le même modèle de données que PostgreSQL (voir `tests/AgentYattus.Api.Tests/ApiFactory.cs`).

**Inconvénients acceptés**

- **SQLite a des limites** : pas de schémas, un seul écrivain à la fois, certaines modifications de tables impossibles en migration. Ça convient à une personne seule.
- **Deux séries de migrations**, une par base, comme le recommande la documentation d'EF Core.
- **Règle à tenir dans le code** : ne pas utiliser de fonction propre à PostgreSQL sans prévoir l'équivalent SQLite.
- **Le téléphone ne joint que le mode serveur.**
- **Rien n'est encore implémenté** : cet ADR fixe la direction. Le sidecar et le jeton arriveront après le MVP.

## 6. Une règle de sécurité qui reste en vigueur

> **Tant que l'authentification n'existe pas, l'API n'écoute que sur la machine locale.**

C'est déjà configuré dans `src/AgentYattus.Api/appsettings.json` :

```json
"AllowedHosts": "localhost;127.0.0.1;[::1]"
```

## 7. Vérifie toi-même

Ouvre `tests/AgentYattus.Api.Tests/ApiFactory.cs` et trouve la ligne qui remplace PostgreSQL par SQLite pendant les tests. Puis lance les tests de l'API :

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Si tout passe, c'est la preuve que le **même code** fonctionne sur les deux bases.

## 8. Quiz

1. Pourquoi ne pas mettre le backend directement à l'intérieur du desktop ?
2. Qu'est-ce qu'un sidecar ?
3. Si l'API n'écoute que sur `127.0.0.1`, pourquoi faut-il quand même un jeton ?
4. Qu'est-ce qui prouve, dès aujourd'hui, que le même code peut tourner sur SQLite et sur PostgreSQL ?

## 9. Pour aller plus loin

- L'ADR complet : `docs/adr/0004-backend-hybride.md`
- Migrations avec plusieurs bases : <https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers>
- Limites de SQLite : <https://learn.microsoft.com/ef/core/providers/sqlite/limitations>
- Adresses d'écoute de Kestrel : <https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints>

<details>
<summary>Réponses du quiz</summary>

1. Parce que le téléphone ne pourrait pas s'y connecter, et qu'un plantage de l'interface arrêterait les missions en cours.
2. Un programme lancé à côté de l'application principale, qui démarre et s'arrête avec elle.
3. Parce que tous les autres programmes de l'ordinateur peuvent joindre `127.0.0.1`. Le jeton garantit que seul le desktop qui a lancé l'API peut l'utiliser.
4. Les tests de l'API : ils remplacent PostgreSQL par SQLite et passent avec le même modèle de données.

</details>
