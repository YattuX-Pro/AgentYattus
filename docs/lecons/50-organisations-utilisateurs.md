# Leçon 50 — Organisations et Utilisateurs : administrer les équipes

> **Commit** : `feat(admin): pages Organisations et Utilisateurs, groupe Administration`
> **Phase** : multi-utilisateur
> **Fichiers** : `src/AgentYattus.Admin/src/app/` (`pages/organizations/` : `organizations.ts`, `organizations.html`, `slug.ts`, `slug.spec.ts`, `pages/users/users.ts`, `pages/projects/projects.ts`, `shell/`, `app.routes.ts`)

## 1. Ce qu'on construit

- **Organisations** : la liste ; pour chacune, son nom, la visibilité par défaut de ses projets, ses membres et leurs rôles ; « Nouvelle organisation » ; le tableau **Qui possède quoi**.
- **Utilisateurs** : l'annuaire, avec le rôle dans l'instance, les organisations, la première et la dernière visite, et un lien vers la console Keycloak pour créer un compte.
- Le groupe **Administration** du menu latéral, annoncé dans la maquette (leçon 40).

## 2. Proposer, sans imposer : l'identifiant

```ts
export function slugify(name: string): string {
  return name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
    .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40).replace(/-+$/, '');
}
```

En tapant « Équipe Été », l'identifiant `equipe-ete` se remplit tout seul. `normalize('NFD')` sépare chaque lettre de son accent (« é » devient « e » suivi d'un accent combinant), puis on retire les accents.

Dès qu'on modifie l'identifiant à la main, il cesse de suivre le nom (`slugEdited`). C'est le principe : **l'écran propose, l'utilisateur décide, le serveur vérifie** (format et unicité, leçon 46).

## 3. Le moindre privilège, dès l'arrivée

```ts
this.api.setOrgMember(org.id, user.id, 'Observer')
```

Un nouveau membre arrive **observateur**. On l'élève ensuite, d'un clic, s'il doit créer des projets ou administrer. Donner trop de droits par défaut, c'est parier que personne n'oubliera de les retirer.

## 4. Afficher les erreurs du serveur telles qu'elles sont

- Retirer le **dernier administrateur** : le serveur refuse (400) avec son message, que l'écran affiche tel quel dans une notification.
- Un identifiant déjà pris : le serveur répond **409**, et l'écran le traduit en « Cet identifiant est déjà pris ».

L'écran ne réécrit pas les règles du serveur : il les **montre**.

⚠️ **Bug trouvé en vérifiant dans le navigateur** : après un refus, la bascule restait sur « Observateur ». Le serveur avait gardé « Administrateur », la valeur liée (`[value]="m.role"`) n'avait donc pas changé, et Angular ne repousse pas une entrée inchangée : le composant gardait son état local. La correction, sur les pages Organisations et Projets :

```ts
} catch (e) {
  toast.error(errorMessage(e));
  this.orgs.set(null);   // on redessine la section
  await this.load();     // à partir de ce que dit le serveur
}
```

Leçon générale : un contrôle qui change d'état **avant** la réponse du serveur doit être remis d'aplomb quand le serveur refuse.

## 5. Ce que l'annuaire n'est pas

L'annuaire ne liste que les personnes **déjà venues**. Créer un compte, réinitialiser un mot de passe, activer la double authentification : tout cela reste dans Keycloak, d'où le lien vers sa console. Inviter par e-mail depuis l'application demanderait un compte de service Keycloak aux droits d'administration (ADR 0009) : une clé très sensible de plus, écartée pour l'instant.

## 6. Quiz

1. Que fait `normalize('NFD')` dans `slugify` ?
2. Pourquoi l'identifiant cesse-t-il de suivre le nom dès qu'on le modifie ?
3. Pourquoi un nouveau membre arrive-t-il observateur ?
4. Pourquoi ne crée-t-on pas les comptes depuis l'espace admin ?

<details>
<summary>Réponses du quiz</summary>

1. Elle sépare chaque lettre de son accent, pour pouvoir retirer les accents ensuite.
2. Pour ne pas écraser ce que l'utilisateur a choisi : l'écran propose, l'utilisateur décide.
3. C'est le moindre privilège : on accorde plus de droits quand c'est nécessaire, pas l'inverse.
4. Il faudrait un compte de service Keycloak avec des droits d'administration : une clé très sensible à protéger, écartée pour l'instant.

</details>
