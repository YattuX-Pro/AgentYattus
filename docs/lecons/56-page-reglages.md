# Leçon 56 — La page Réglages : un formulaire, un brouillon, un seul envoi

> **Commit** : `feat(admin): page Réglages`
> **Phase** : compléter l'admin
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`pages/settings/` : `settings.ts`, `settings.html`, `settings.spec.ts`, `core/api.ts`, `core/format.ts`, `format.spec.ts`, `core/events.ts`, `shell/`, `app.routes.ts`)

## 1. Ce qu'on construit

La page **Réglages** du groupe Administration, en quatre cartes : Exécution, Budget (avec la barre du mois), Espace de travail (taille des fichiers, fichiers exclus), Sécurité et serveur (rétention, et en lecture seule : autorité Keycloak, export des traces, dossier par défaut).

## 2. Un brouillon, puis Enregistrer

```ts
protected readonly form = signal<UpdateSettings | null>(null);

protected set<K extends keyof UpdateSettings>(key: K, value: UpdateSettings[K]) {
  this.form.update((f) => (f ? { ...f, [key]: value } : f));
}
```

Contrairement aux pages Modèles ou Projets, où chaque clic s'enregistre, les réglages forment un **ensemble** : on les modifie, puis on envoie tout d'un coup. Le signal `form` est un **brouillon** ; **Annuler** le recharge depuis le serveur.

Le type `K extends keyof UpdateSettings` garantit qu'on ne peut écrire que des champs qui existent, avec le bon type de valeur.

## 3. Ce que l'écran garde pour lui

- Les **fichiers exclus** s'écrivent un par ligne dans une zone de texte. `parsePatterns` les transforme en liste, sans lignes vides ni doublons. C'est une fonction pure, testée.
- Le **budget** s'écrit « 20 » ou « 20,5 » : on accepte la virgule française, et le vide veut dire « aucun ».

Les valeurs (bornes, motifs sans `/`) sont revérifiées par le serveur, qui renvoie un message par champ. L'écran les affiche dans un encadré.

## 4. Montrer ce qu'on ne règle pas ici

L'autorité Keycloak, l'export OpenTelemetry et le dossier par défaut viennent de la configuration du serveur. Les afficher **en lecture seule** répond à la question « qu'est-ce qui est configuré ? » sans laisser croire qu'on peut le changer depuis le navigateur.

## 5. Quiz

1. Pourquoi les réglages s'enregistrent-ils d'un bloc, alors que la visibilité d'un projet s'enregistre au clic ?
2. Que garantit `K extends keyof UpdateSettings` ?
3. Pourquoi afficher des valeurs qu'on ne peut pas modifier ?

<details>
<summary>Réponses du quiz</summary>

1. Les réglages vont ensemble (et sont validés ensemble) ; un clic sur la visibilité est une décision isolée.
2. Qu'on ne peut écrire qu'un champ existant, avec une valeur du bon type.
3. Pour que l'administrateur sache ce qui est configuré, sans croire qu'il peut le changer ici.

</details>
