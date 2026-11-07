# Leçon 81 — La page Portraits : envoyer des fichiers, prévisualiser, survoler

> **Commit** : `feat(admin): page Portraits`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/app/pages/portraits/`, `core/api.ts`, `app.routes.ts`, `shell/nav.ts`

## 1. Ce qu'on construit

Configuration › **Portraits** : la galerie en cartes. Au survol, la pose « travaille » cède la place à la pose « parle » (fondu de 500 ms). On renomme un prénom suggéré, on masque un portrait du recrutement, on le retire, ou on en ajoute un.

## 2. Envoyer des fichiers : `FormData`

```ts
const form = new FormData();
form.append('travail', fichier);
this.http.post('/api/team/portraits', form);
```

Sans en-tête `Content-Type` à la main : le navigateur écrit le `multipart/form-data` et sa frontière lui-même. Le serveur, lui, ne croit pas le type annoncé : il lit la signature (leçon 72).

## 3. Prévisualiser sans envoyer

`URL.createObjectURL(fichier)` donne une adresse locale à afficher tout de suite dans une balise `<img>`. On la libère (`revokeObjectURL`) quand on change de fichier, sinon le navigateur garde l'image en mémoire.

## 4. Les images, sans jeton

Une balise `<img>` ne sait pas envoyer le jeton de connexion : c'est pour cela que les images des portraits sont publiques (leçon 72). La liste, elle, exige d'être connecté, et la modifier, d'être administrateur.

## 5. Retirer ou masquer

Un portrait utilisé (par un collègue, même ancien, ou par les réglages) ne se supprime pas : le serveur répond 409 et l'admin affiche son message, « désactive-le plutôt ». Masquer garde l'historique intact.

## 6. Quiz

1. Pourquoi ne pas écrire soi-même l'en-tête Content-Type d'un envoi FormData ?
2. Pourquoi libérer une adresse créée par createObjectURL ?

<details>
<summary>Réponses du quiz</summary>

1. Le navigateur doit y ajouter la frontière (boundary) qui sépare les parties ; écrit à la main, il manquerait.
2. Tant qu'elle existe, le navigateur garde le fichier en mémoire.

</details>
