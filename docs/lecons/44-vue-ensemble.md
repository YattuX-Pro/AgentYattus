# Leçon 44 — La vue d'ensemble : des chiffres calculés, et un graphique à l'échelle

> **Commit** : `feat(admin): vue d'ensemble (indicateurs, graphique, décisions, activité)`
> **Phase** : espace admin web
> **Fichiers** : `src/AgentYattus.Admin/src/app/pages/overview/` (`overview.ts`, `overview.html`, `stats.ts`, `stats.spec.ts`), `app.routes.ts`

## 1. Ce qu'on construit

La page d'accueil de l'admin :

- six **indicateurs** : missions, taux de réussite, écritures à approuver (en laiton s'il y en a), appels d'outils, bascules, connexions ;
- le **graphique** des missions terminées et échouées sur 14 jours ;
- **À décider** et **En cours**, avec un lien vers chaque mission ;
- les **modèles utilisés**, la configuration en un coup d'œil et l'activité récente.

## 2. Pas de route de statistiques

On aurait pu ajouter `GET /stats` à l'API. On ne l'a pas fait : les 100 dernières missions et les 500 derniers événements suffisent à **tout calculer dans le navigateur**.

```ts
const [missions, audit, llm] = await Promise.all([this.api.missions(), this.api.audit(500), this.api.llm()]);
```

`Promise.all` lance les trois requêtes **en même temps** : la page attend la plus lente, pas la somme des trois.

⚠️ La limite est assumée : au-delà de 100 missions, les chiffres portent sur les plus récentes. Le sous-titre de la page le dit. Une route de statistiques viendra le jour où il faudra des chiffres sur un an.

## 3. Une fonction pure, testée

```ts
export function stats(missions: Mission[], audit: AuditFeedEntry[], now = new Date(), days = 14) { ... }
```

`now` est un **paramètre** : le test fixe la date (`new Date(2026, 10, 6)`) et vérifie le rangement par jour. Sans ce paramètre, le test dépendrait du jour où on le lance.

## 4. Un graphique à l'échelle

```ts
protected readonly y = (v: number) => H - B - (v / this.ticks().at(-1)!) * (H - B - T);
```

**Une seule fonction** place les barres, les graduations et les libellés : impossible qu'une barre de 12 dépasse la graduation 10. `axis()` choisit un maximum rond (5, 10, 20, 25, 50…) pour que les graduations tombent sur des nombres lisibles.

Les couleurs viennent des **variables du thème** (`var(--primary)`, `var(--muted-foreground)`) : le graphique suit le thème clair ou sombre sans une ligne de plus.

Le graphique est un **SVG écrit à la main**, sans bibliothèque. Pour une vingtaine de rectangles, une bibliothèque de graphiques serait plus lourde que le graphique lui-même.

## 5. Quiz

1. Pourquoi la page n'a-t-elle pas besoin d'une route `/stats` ?
2. Pourquoi `stats()` reçoit-elle la date du jour en paramètre ?
3. Que garantit le fait qu'une seule fonction `y()` place tout le graphique ?

<details>
<summary>Réponses du quiz</summary>

1. Les missions et le journal récents suffisent à calculer les indicateurs dans le navigateur ; une route viendra quand il faudra des chiffres sur une longue période.
2. Pour que le test donne le même résultat quel que soit le jour où on le lance.
3. Que les barres, les graduations et les libellés partagent la même échelle : rien ne peut être décalé.

</details>
