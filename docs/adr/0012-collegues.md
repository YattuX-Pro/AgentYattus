# ADR 0012 — Les agents comme collègues : portraits, équipe et scène gérés dans l'admin

- **Statut** : proposé
- **Date** : 2026-11-07

## Contexte

Le logiciel de bureau montrait une mission sous forme de graphe. On veut la montrer comme un **open space** : les agents sont des collègues, avec un visage, un prénom et une personnalité ; ils se parlent dans des bulles, se passent des dossiers, et frappent à la porte de l'humain quand ils ont besoin de son accord. On veut aussi pouvoir **recruter** un collègue depuis le bureau.

Contrainte posée : **rien en dur**. Les visages, les prénoms, l'équipe de départ, les traits de personnalité et les phrases des bulles doivent se gérer dans l'espace admin web (ADR 0008), pas dans le code.

## Décision

1. **Un collègue = un rôle existant + un portrait + un prénom + des traits.** Il n'invente pas de nouveau rôle : ses outils et son modèle sont **hérités du rôle**, réglés dans l'admin (pages Agents et Modèles). Les rôles sur mesure (« poste personnalisé ») feront l'objet d'une décision à part (ADR 0011, point 7).
2. **Un siège par rôle et par projet.** Recruter un analyste pour un projet remplace l'analyste précédent ; l'ancien reste dans l'historique (qui, quand).
3. **Tout ce qui est contenu est une donnée gérée dans l'admin** (module Équipe, schéma `team`) :
   - la **galerie de portraits** (pose « travaille » et pose « parle », prénom suggéré, actif ou non), stockée en base ;
   - le **chef d'équipe** (le personnage qui annonce le début et la fin d'une mission) et le portrait de **« Vous »** ;
   - l'**équipe de départ** d'un nouveau projet ;
   - les **traits de personnalité** : un libellé et la phrase de style ajoutée à la consigne ;
   - les **répliques** : un modèle de phrase par type d'événement, avec des variables (`{fichier}`, `{destinataire}`…) ;
   - la **vitesse de la scène** (écriture des bulles, pauses).
4. **Ce qui reste dans le code**, parce que c'est du comportement et non du contenu : quel événement fait parler qui, quel objet circule, les animations, et toutes les règles de sécurité.
5. **Les bulles viennent des événements de la mission**, mis en forme par les répliques. Aucun appel supplémentaire au modèle : c'est gratuit, et ce qui est affiché est toujours vrai.
6. **La personnalité entre dans la consigne**, entre le texte du rôle et les garde-fous. Les garde-fous restent ajoutés **en dernier** par le code (ADR 0011) : un trait ne peut pas les retirer.
7. **Droits** : les administrateurs de l'instance gèrent la galerie, les traits, les répliques et les réglages ; recruter demande le droit de lancer des missions dans le projet (propriétaire ou éditeur, ADR 0009). Les images des portraits sont lisibles sans connexion : ce sont des illustrations, pas des données.
8. **Envoi d'images contrôlé** : PNG ou WebP seulement, vérifiés par leur signature (pas par l'extension), 2 Mo au plus par pose.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Personnages dessinés en SVG, personnalisables pièce par pièce | Personnalisation infinie, rien à stocker | Peu humains (retour de l'utilisateur) |
| Portrait généré à chaque recrutement | Unique | 30 à 60 s et un coût à chaque fois, résultat imprévisible |
| **Galerie de portraits gérée dans l'admin** | Instantané, cohérent, coût payé une fois | Choix limité à la galerie (qu'on peut enrichir) |
| Phrases des bulles rédigées par le modèle | Vivant | Des tokens à chaque bulle, et un texte qui peut être faux |
| **Répliques modèles, éditables** | Gratuit, toujours vrai | Moins varié |

## Conséquences

- Un module de plus (Équipe) et quatre pages dans l'admin web.
- Les événements de mission disent désormais **quel rôle** a utilisé **quel outil sur quel fichier** (événement `action:…`), pour que la scène sache qui parle.
- L'écran Mission du bureau (le graphe) est remplacé par l'open space ; la logique de décision (relire tout avant d'approuver) est conservée telle quelle.
- La galerie générée pour la maquette (22 personnes, deux poses) est livrée dans `deploy/portraits/` et s'importe au premier démarrage, si la galerie est vide (`Team:SeedFolder`).
