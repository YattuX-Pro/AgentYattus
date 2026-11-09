# Proposition UX — projets, agents et conversations

Analyse du 9 novembre 2026. Proposition à valider, pas une refonte déjà appliquée.

## Décision proposée

Un projet possède un espace stable, une liste de conversations, une équipe et des livrables. Le chat est le lieu principal de travail. Le bureau animé reste une vue facultative de l’équipe, jamais une condition pour lire ou répondre. Un recrutement RH reste une conversation ; sa fiche n’apparaît qu’à la demande ou quand elle est prête.

Trois notions doivent rester distinctes : l’interlocuteur choisi par la personne, les participants réels à chaque message et l’agent exécutant une tâche. Un passage de relais ne doit jamais réécrire l’identité des anciens messages.

## Constats vérifiés dans le code

- `MissionTurn` et `MissionTurnDto` enregistrent le texte, la date et un booléen humain/agent. Aucun identifiant de collègue, prénom ou portrait historique n’est attaché au message.
- `ReportsViewModel.LoadTurnsAsync` choisit un seul auteur avec `report.Faces.LastOrDefault()` et l’applique à toutes les réponses passées.
- `OfficeViewModel.LoadConversationAsync` applique également l’auteur courant à toutes les réponses. `Author` et `Worker` dépendent notamment du dernier événement `delegue:`.
- Après une délégation ordinaire, `AgentFrameworkRunner` remplace le rôle de la conversation sauvegardée par celui du collègue délégué. Une reprise peut donc réellement s’adresser à un autre agent, au-delà du défaut d’affichage.
- Les visages des rapports sont retrouvés à partir de la mission et de l’équipe actuelle, pas de l’auteur historique du message.
- `ShellViewModel.OpenRecruitmentAsync` remplace la vue courante par le formulaire de recrutement. Le chat RH et les réglages manuels se retrouvent réunis dans ce parcours.
- `ShowNav` masque la navigation globale dans l’open space et le recrutement. `OpenOfficeAsync` sélectionne une mission en attente, active ou récente plutôt qu’un espace de projet stable.
- Les événements et fragments de streaming ne portent que la mission et son statut ou texte. Ils ne donnent pas explicitement l’auteur, le message ni l’exécution concernés.
- Le client reçoit les événements via SignalR mais aucun centre persistant de notifications/non-lus n’a été trouvé. Les abonnements sont concentrés sur l’écran actuellement ouvert. La reconnexion automatique ne suffit pas à récupérer les événements manqués.

Conséquence : le problème d’identité est à la fois un défaut de stockage, un défaut de routage et un défaut de restitution. Modifier seulement les avatars ou ajouter des animations ne le résoudrait pas.

## Structure visible

Barre stable : projet actif, nom, état de connexion et accès à l’activité. À gauche : conversation avec le chef, conversations directes avec les collègues, RH et conversations liées aux tâches. Au centre : un seul fil. En haut du fil : prénom, rôle, projet, état réel et destinataire du prochain message. Les livrables et l’équipe se consultent dans le même espace avec un onglet ; le contexte et les brouillons sont conservés.

Le chef est le point d’entrée proposé, sans être obligatoire. Un clic sur un collègue ouvre son fil direct existant. On ne crée pas un fil neuf à chaque ouverture du logiciel.

« Rapports » devient « Livrables » : documents, analyses et résultats, avec auteur et version. « Discuter de ce résultat avec Inès » ouvre la conversation liée au livrable. Cette action ne crée pas un deuxième système de chat.

Sur une petite fenêtre : la liste des conversations se replie ; un bouton explicite la rouvre. Un détail n’est ouvert qu’à la demande. Pas de troisième panneau permanent.

## Parcours de bout en bout

1. Créer ou ouvrir le projet. Nom obligatoire ; dossier facultatif pour discuter, nécessaire pour les outils de fichiers. Serveur utilisé sans écran de choix de stockage. Projet et dossier restent visibles et modifiables au même endroit.
2. Décrire le résultat au chef ou s’adresser directement à un agent. Le champ indique « Message à Inès · Analyste ». Entrée envoie ; Maj + Entrée ajoute une ligne. L’envoi affiche immédiatement le message et son accusé de réception.
3. Pour une demande complexe, le chef propose un plan court : livrable, agent responsable, étapes et validations. Il n’impose pas un plan pour une salutation ou une question simple. L’utilisateur peut ajuster la demande avant une opération soumise à accord.
4. Les agents exécutent leurs tâches liées au même projet. Les passages de relais apparaissent comme des événements sobres et explicites. L’utilisateur peut ouvrir les détails sans subir les traces d’outils et les échanges internes bruts.
5. Pendant le travail, le fil affiche le dernier état réellement reçu. L’utilisateur peut changer de conversation ou quitter l’application. Le serveur continue selon les limites de durée configurées.
6. Une question ou une demande d’accord met la tâche « À vous ». L’action indique ce qui est demandé, pourquoi et ce qui se passera ensuite. Une précision n’est pas présentée comme une autorisation d’écrire un fichier.
7. Le résultat arrive en streaming, puis devient un message enregistré et éventuellement un livrable. « Réponse reçue » et « Livrable validé » restent deux états différents.
8. L’utilisateur valide ou demande une correction. Le chef rassemble les livrables lorsqu’il coordonne la tâche ; il ne remplace pas l’auteur réel.
9. Au retour dans le projet : conversations et brouillons restaurés, non-lus visibles, tâches en cours actualisées et questions en attente regroupées. L’historique reste attribué aux auteurs d’origine même si l’équipe a changé.

## Règles de conversation

- Fil direct avec l’analyste : destinataire stable. Une aide d’un autre agent devient une tâche liée. S’il intervient dans le fil, son message porte son propre prénom et son rôle ; le champ de saisie reste adressé à l’analyste.
- Fil du chef : le chef conserve la coordination. Exemple : « Je demande à Malik de vérifier les contraintes. » Puis « Malik a terminé la vérification » et une contribution signée ou un résumé du chef clairement présenté comme tel.
- Transfert volontaire : action explicite « Continuer avec Malik ». Le fil précédent reste accessible. Aucun changement silencieux du destinataire.
- Agent retiré : l’historique reste lisible. Le système propose un remplaçant, sans lui attribuer les messages antérieurs.
- Agent et fournisseur sont distincts : une bascule entre modèles gratuits ne change ni le collègue, ni son prénom, ni l’auteur affiché.
- Messages internes : afficher les demandes transmises, réponses utiles et blocages. Ne pas simuler de conversations ou afficher un raisonnement caché comme s’il s’agissait d’un travail vérifié.

## Recrutement RH dans le même cadre

Scénario : « Il y a un siège libre, demande au RH d’ajouter un analyste. »

Le chef confirme dans son fil : « J’ai confié la préparation au RH. » Une ligne liée indique « RH · Préparation du profil » et « Ouvrir ». L’écran ne change pas de lui-même.

La conversation RH apparaît dans la liste avec le même projet. Le RH y pose au maximum les précisions nécessaires. Quand le profil est prêt, une carte compacte affiche poste, mission, compétences principales et l’action « Ajouter à l’équipe ». « Voir les détails » ouvre les consignes, outils et compétences dans un panneau temporaire. Le choix du visage et du prénom intervient à cette étape, pas avant la clarification du besoin.

Après confirmation : une seule création, une confirmation dans le fil RH, une mise à jour de l’équipe et une information au chef. Pas d’annonce « recruté » si seule la fiche est préparée. Si le poste ou les droits ne permettent pas l’ajout, le problème apparaît dans cette carte avec une action précise. Toute tentative répétée réutilise la même demande pour éviter les doublons.

## États lisibles

| État réel | Texte proposé | Action |
| --- | --- | --- |
| En file | Inès attend un créneau | Annuler |
| Appel fournisseur envoyé, aucun contenu reçu | Inès prépare sa réponse | Arrêter |
| Outil de lecture actif | Inès consulte les documents | Voir l’activité |
| Outil de vérification actif | Malik vérifie les résultats | Voir l’activité |
| Premiers fragments reçus | Inès écrit… | Arrêter |
| Information manquante | Inès a une question pour vous | Répondre |
| Autorisation requise | Votre accord est nécessaire | Examiner |
| Réponse enregistrée | Inès a répondu · Non lu | Ouvrir |
| Fournisseur lent ou bascule | Le fournisseur tarde ; essai d’un autre modèle gratuit | Arrêter |
| Réseau du client perdu | Connexion interrompue · état à actualiser | Réessayer |
| Échec confirmé | La tâche n’a pas abouti | Voir la cause / Relancer |

Pas de faux pourcentage, pas de minuterie présentée comme une estimation fiable et pas d’« analyse en cours » si aucun événement ne la confirme. Une réponse lente peut rester lente : l’interface doit informer, sans fabriquer un progrès.

L’état « travail terminé » est séparé de « réponse lue ». Les tâches peuvent attendre une information ou une validation ; les notifications peuvent être non lues indépendamment de l’exécution.

## Notification avatar et bulle nuage

Notification interne : portrait de l’auteur réel à gauche, nuage à droite, pointe ancrée près de sa bouche. Le rôle, le projet et le motif restent écrits : le portrait seul ne suffit pas. Exemple : « Inès · Analyste — Mon analyse est prête. » Actions « Lire » et fermer. Pour une question : « J’ai besoin d’une précision. » Action « Répondre ».

- Une entrée discrète, environ 180–220 ms. Le nuage se déploie une fois ; pas de rebond, de pulsation ni de bouche animée en continu.
- Le point d’ancrage est une donnée du portrait ; il ne suit pas une position arbitraire commune à toutes les images. La pointe ne doit pas masquer le visage.
- Une seule notification visible ; les suivantes sont regroupées dans l’activité. Une fois par réponse finalisée, pas une fois par fragment, outil ou agent interne.
- Si le fil concerné est visible au dernier message, pas de toast concurrent : la réponse apparaît directement. Si la personne lit plus haut, conserver sa position et montrer « Nouvelle réponse ».
- Ailleurs dans l’application : toast et badge non lu. La fermeture du toast ne marque pas la conversation comme lue. Un non-lu ne disparaît qu’après exposition effective du message dans le fil.
- Le toast disparaît après environ huit secondes, avec pause au survol ou au focus ; la notification reste dans le centre d’activité. Une demande d’accord reste visible dans « À vous » même après fermeture du toast.
- Pas de son par défaut. Mode mouvements réduits : pas de déplacement, apparition simple. Navigation clavier, noms accessibles et annonce polie de la nouvelle réponse.
- Application fermée : l’événement reste enregistré sur le serveur. Notification système facultative si autorisée et techniquement prise en charge ; le nuage personnalisé n’est garanti que dans l’application.
- Aperçu confidentiel : possibilité de masquer le contenu, en conservant auteur et projet. Les droits sont revérifiés à l’ouverture ; un événement d’un ancien projet ne donne pas accès à son contenu.

## Animations utiles

Le bureau animé illustre l’état réel de l’équipe, sans être la source des identités ni des délais. Pas de déplacement des collègues pour simuler chaque message. Une transition courte entre deux contextes, un indicateur discret d’activité et l’arrivée du nuage suffisent. Aucun effet de survol ne change la taille d’une cible cliquable. Le texte reçu s’affiche au rythme du flux ; aucune seconde animation ne ralentit artificiellement sa lecture. L’autodéfilement ne reprend pas la main pendant la lecture d’anciens messages.

## Fondations nécessaires

Conserver les missions pour l’exécution ; ajouter une couche de conversations stable :

- Conversation : projet, participants, interlocuteur principal, sujet, lien vers la tâche et brouillon par personne.
- Message : identifiant, auteur réel, identifiant immuable du collègue, rôle, nom et portrait au moment de l’envoi, horodatage, type, conversation, réponse éventuelle à un message et statut d’envoi. La clé du rôle ne suffit pas si plusieurs collègues peuvent exercer le même métier.
- Tâche/exécution : responsable, parent éventuel, état, questions, validations, livrables et version de l’exécution.
- Streaming : conversation, message, auteur, exécution et séquence pour rattacher, ordonner et dédupliquer les fragments.
- Événement durable : demande acceptée, activité réellement observée, relais créé, question posée, autorisation demandée, réponse enregistrée, tâche échouée.
- Notification/non-lu : destinataire, événement, conversation, message cible, dates de lecture et de fermeture du toast. Ces deux dernières actions sont différentes.

Une reconnexion récupère les événements depuis le dernier curseur, puis resynchronise les états. Les événements sont dédupliqués. Les notifications partent après enregistrement de la réponse, jamais avant. Des versions d’exécution empêchent un ancien fragment ou une fin d’appel retardée d’écraser la nouvelle réponse.

Pour les anciens messages sans auteur, ne pas inventer : restaurer à partir de données fiables quand elles existent ; sinon afficher « Agent — identité historique non disponible ». Un unique événement de délégation ne suffit pas à attribuer tous les messages d’une conversation complexe.

## Ordre conseillé

1. Fiabilité des identités et du destinataire, y compris après délégation, retour dans Rapports et changement d’équipe. Migrations additives et restitution prudente des historiques.
2. Un composant de conversation commun, un espace projet persistant et le RH sans remplacement de l’espace de travail. Livrables séparés du chat.
3. États fiables, questions et accords différenciés, notifications durables, non-lus et resynchronisation au retour.
4. Nuage avec avatar, gestes de navigation et animations sobres. Vérification sur petite fenêtre, clavier et mouvements réduits.

## Critères d’acceptation

- Après fermeture et réouverture, les mêmes messages portent les mêmes noms et portraits.
- Une conversation directe reste adressée au même collègue, même après intervention d’un spécialiste.
- La création d’une demande RH ne remplace pas automatiquement l’écran ni la conversation en cours.
- Une fiche de recrutement n’affiche pas tous les réglages techniques avant d’en avoir besoin.
- Un message envoyé une fois ne déclenche qu’une tâche ; une répétition de confirmation ne recrute pas deux collègues.
- Une réponse arrivée pendant une absence reste accessible et non lue au retour.
- Après reconnexion, aucune notification ni réponse n’est perdue ou dupliquée.
- Le clic sur le nuage ouvre le bon projet, le bon fil et le bon message.
- Une demande d’accord ne disparaît pas avec le toast.
- La bascule entre fournisseurs gratuits conserve l’identité du collègue et l’historique.
- Entrée envoie, Maj + Entrée ajoute une ligne, les brouillons survivent à la navigation.
- L’application reste utilisable sans animation, à petite largeur et entièrement au clavier.

## Repères de conception

Ces choix sont adaptés au produit, avec comme repères les recommandations Apple sur le retour d’état et les notifications :

- https://developer.apple.com/design/human-interface-guidelines/feedback
- https://developer.apple.com/design/human-interface-guidelines/progress-indicators
- https://developer.apple.com/design/human-interface-guidelines/notifications

Les noms, délais d’animation et scénarios de la démonstration sont des propositions de conception ; ils ne constituent pas une mesure des performances ni une refonte déjà installée.

## Mise en place et ajustement du 9 novembre 2026

Le choix confirmé ensuite est **un seul groupe par projet**, sur le principe d'un groupe WhatsApp. Le fil affiche les messages humains, les interventions des agents et les transmissions au RH, avec leurs auteurs conservés. Le sélecteur du composeur indique l'agent sollicité ; il ne change pas le fil ni le brouillon. Le chef d'équipe est choisi par défaut lorsqu'il existe. Une seule demande est lancée par envoi : les autres agents interviennent sur délégation, sans appel systématique de toute l'équipe.

L'activité et les livrables restent deux vues du même espace. Les demandes en cours et les accords à donner sont accessibles depuis le groupe. La fiche RH et la confirmation du prénom/portrait s'affichent dans cet espace. Les réponses non lues sont enregistrées sur le serveur ; fermer le nuage ne les marque pas lues. Le nuage montre le portrait de l'auteur et sa bulle pointe vers l'ancrage de bouche du portrait ; son apparition respecte la réduction des animations macOS.

Les agents reçoivent au maximum douze extraits récents du projet, limités à 450 caractères chacun. Ce contexte de groupe est envoyé comme contexte cité et n'est pas accumulé dans l'historique conservé à chaque reprise. L'historique affiché et ce contexte envoyé aux modèles sont distincts. Les messages anciens restent en base ; l'affichage est borné aux 500 demandes et 1 000 tours les plus récents après chargement des messages précédents.

Les notifications sont internes à l'application : lorsque celle-ci est fermée, le serveur conserve les réponses pour le prochain retour, sans notification système. Les messages antérieurs à l'enregistrement des identités sont indiqués comme historiques lorsque l'auteur ne peut pas être établi avec certitude.

### Présentation du groupe inspirée de WhatsApp

Bulles personnelles vertes alignées à droite, interventions des agents à gauche avec leur avatar et leur nom, heure discrète, en-tête de groupe unique et composeur compact. Le sélecteur d'agent reste dans la barre de saisie. L'indicateur de travail affiche les agents réellement en cours ; après réception de texte il indique « écrit », puis revient à « travaille » si aucun fragment n'arrive pendant huit secondes. Trois points s'animent pendant l'activité. L'animation s'arrête à la fin de la tâche, lorsque la vue est quittée et reste fixe si macOS demande une réduction des animations.
