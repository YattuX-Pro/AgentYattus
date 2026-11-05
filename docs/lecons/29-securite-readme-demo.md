# Leçon 29 — Sécurité, README et démo

> **Commit** : `docs: sécurité, README et démo du MVP`
> **Phase** : 4, MVP
> **Fichiers** : `README.md`, `docs/securite.md`, `docs/demo.md`

## 1. Ce qu'on construit

Trois documents qui transforment un projet qui fonctionne en un projet **qu'on peut présenter et reprendre** :

| Document | Pour qui | Il répond à |
|---|---|---|
| `README.md` | Quelqu'un qui découvre le dépôt | « C'est quoi ? Comment je le lance ? Où est quoi ? » |
| `docs/securite.md` | Un relecteur, un futur contributeur | « Qu'est-ce qui est protégé, contre quoi, et où dans le code ? » |
| `docs/demo.md` | Toi, devant un public | « Que montrer, dans quel ordre, et que répondre ? » |

## 2. Un modèle de menaces

Un **modèle de menaces** (*threat model*) liste ce qu'on protège, les attaques possibles, et la parade de chacune, **avec l'endroit du code** où elle se trouve. Le nôtre suit le **OWASP Top 10 pour les applications LLM**, la référence du domaine. Quatre de ses risques nous concernent directement :

| Risque | En une phrase |
|---|---|
| **LLM01 Injection de prompt** | Du texte lu par l'agent (un fichier, une page web) lui donne des ordres |
| **LLM02 Divulgation d'informations sensibles** | L'agent répète un secret qu'il a lu |
| **LLM06 Agentivité excessive** | L'agent a plus de pouvoirs que nécessaire, et agit sans contrôle |
| **LLM10 Consommation non bornée** | L'agent tourne et coûte sans limite |

### Récapitulatif des protections construites dans ce cours

| Protection | Leçon |
|---|---|
| Barrière du workspace (chemins, `..`, chemins absolus) | 03 |
| Limite de tours, signalée dans l'audit | 04, 08, 17 |
| Clé hors du dépôt (user-secrets), tests qui ne peuvent pas appeler le vrai modèle | 05, 17 |
| API sur la seule boucle locale | 11, ADR 0004 |
| Refus des liens symboliques | 17 |
| Réponses hors des journaux et de la télémétrie | 19, 21 |
| Approbation humaine avant chaque écriture, décision atomique | 20 |
| Décision liée à la demande affichée, lecture complète exigée, caractères invisibles rendus visibles | 25 |
| Moindre privilège : l'Analyste ne peut pas écrire | 27 |

Une chose frappe : **la plupart de ces protections ont été trouvées en route** (revues de sécurité automatiques, tests, rendu de l'écran). La sécurité n'est pas une étape finale : c'est une attention de chaque commit.

### Écrire honnêtement les limites

Le document liste aussi ce qui **n'est pas** protégé (pas encore d'authentification, *TOCTOU* local, file en mémoire). Un modèle de menaces qui ne montre que les forces donne une fausse confiance ; les limites écrites indiquent où travailler ensuite.

## 3. Un bon README

1. **La promesse en une phrase**, en tête : « Un poste de contrôle pour agents IA ».
2. **Démarrer** en quelques commandes copiables.
3. **Tester**, avec la précision importante : aucun test n'appelle le vrai modèle.
4. **Une carte** des dossiers, et des liens vers les décisions (ADR), la sécurité, la démo.

Le README ne réexplique pas tout : il **oriente**.

## 4. Une démo qui raconte une histoire

`docs/demo.md` déroule une revue complète en neuf étapes. Chacune montre **une** idée du produit (temps réel, relais entre agents, approbation, traçabilité, observabilité). Les questions probables du public y figurent, avec leur réponse.

La démo prévoit aussi la **variante du refus** : montrer qu'on peut dire non, et que rien n'est alors écrit, convainc souvent plus que l'approbation.

## 5. Vérifie toi-même

Suis `docs/demo.md` de bout en bout. Si une étape ne se passe pas comme écrit, c'est le document **ou** le code qu'il faut corriger.

## 6. Quiz

1. Qu'est-ce qu'un modèle de menaces ?
2. Pourquoi y écrire aussi les limites connues ?
3. Quel risque OWASP le moindre privilège de l'Analyste réduit-il ?
4. Pourquoi montrer le refus pendant la démo ?

## 7. Pour aller plus loin

- OWASP Top 10 pour les applications LLM : <https://genai.owasp.org/llm-top-10/>

<details>
<summary>Réponses du quiz</summary>

1. La liste de ce qu'on protège, des attaques possibles, et de la parade de chacune, avec l'endroit du code où elle se trouve.
2. Pour ne pas donner une fausse confiance, et pour indiquer où travailler ensuite.
3. LLM06, l'agentivité excessive (et il limite l'impact d'une injection de prompt, LLM01).
4. Parce que prouver qu'on peut dire non, et que rien n'est alors écrit, montre que le contrôle humain est réel.

</details>
