# Leçon 67 — La chaîne gratuite : montrer ce qui s'est passé, faute de connaître le quota

> **Commit** : `feat(admin): activité du jour de la chaîne gratuite`
> **Phase** : compléter l'admin
> **Fichiers** : `src/AgentYattus.Admin/src/app/pages/models/` (`logic.ts`, `logic.spec.ts`, `models.ts`, `models.html`)

## 1. Ce qu'on construit

Une colonne **Aujourd'hui** dans la chaîne gratuite : pour chaque modèle, les **prises de main** du jour, les **bascules reçues** (en laiton), et l'heure de la dernière.

## 2. Ne pas inventer un chiffre

La maquette montrait une barre « quota du jour : 92 % ». C'est séduisant, mais **aucun fournisseur gratuit ne publie le quota restant** d'une clé. Afficher une barre aurait été inventer un chiffre.

On montre donc ce que l'on **sait**, parce qu'on l'a écrit dans le journal :

- `modele:nvidia/llama…` : un agent a commencé avec ce modèle ;
- `bascule:nvidia/llama…` : le modèle précédent a épuisé son quota, la chaîne est passée à celui-ci.

Un modèle qui reçoit beaucoup de bascules signale que ceux d'avant s'épuisent tôt dans la journée : c'est l'information utile pour réordonner la chaîne.

## 3. Une fonction pure, avec la date en paramètre

```ts
export function chainActivity(model: string, audit: AuditFeedEntry[], now = new Date()): ChainActivity
```

Comme les statistiques de la vue d'ensemble (leçon 44), la date du jour est un paramètre : le test la fixe, et vérifie qu'un événement d'hier ne compte pas.

## 4. Quiz

1. Pourquoi ne pas afficher une barre de quota ?
2. Que signifie un modèle qui reçoit beaucoup de bascules ?

<details>
<summary>Réponses du quiz</summary>

1. Les fournisseurs ne disent pas le quota restant : la barre serait un chiffre inventé.
2. Que les modèles placés avant lui épuisent leur quota tôt : il faudrait peut-être réordonner la chaîne.

</details>
