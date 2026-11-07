# Leçon 123 — Relire une modification en diff

> **Commit** : `feat(desktop): relecture en diff, lignes retirées et ajoutées`
> **Phase** : C
> **Fichiers** : `Client.Core/{MissionViewModel,Scene,RecruitViewModel}.cs`, `Desktop/Views/{OfficeView.axaml,Avatar.cs}`, `tests/.../SceneTests.cs`

## 1. Ce qu'on construit

Quand l'agent veut modifier un passage, la demande d'accord montre **seulement ce qui change** :

```
− var x = t.Result;
+ var x = await t;
```

Les lignes retirées sont en cinabre, les lignes ajoutées en vert-de-gris (`Tone.Diff`). Un fichier écrit en entier (`write_file`, `write_document`) s'affiche en lignes « + ». Comme avant, « Approuver » attend que tout ait été affiché. Les caractères invisibles restent rendus visibles (leçon 26).

`ApprovalItem` garde les lignes brutes (`Preview`, `AllLines`). Les lignes à l'écran (`ShownPreview`, `ShownLines`) portent le préfixe : les vérifications existantes ne changent pas.

Dans l'open space, une recherche se raconte (« Je cherche « order.Result » dans le code. »). Une modification se raconte avec la décision, comme une écriture.

## 2. Vérifie toi-même

`dotnet test --filter An_edit_is_reviewed` : un `edit_file` donne « − » puis « + », et un `write_file` donne des « + ».

## 3. Quiz

1. Pourquoi ne pas montrer le fichier entier après modification ?

<details>
<summary>Réponse</summary>

1. On ne relit vraiment que ce qui change. Un diff de 3 lignes se vérifie, alors qu'un fichier de 400 lignes se survole.

</details>
