# Recruter avec l'agent RH

Dans **Nouveau collègue**, ouvrir **Agent RH · Aidez-moi à créer le bon agent** et décrire le travail à déléguer avec ses propres mots. Aucun prompt ni fichier de compétence n'est nécessaire.

Le RH demande les précisions indispensables, jusqu'à trois questions courtes par réponse. Lorsque le besoin est clair, il prépare :

- le nom et le résumé du poste ;
- une consigne qui décrit les objectifs, les livrables, la méthode, les vérifications et les limites ;
- les outils utiles parmi ceux réellement disponibles ;
- une à trois compétences réutilisables, avec leur contexte d'utilisation et leur procédure.

Ses réponses s'affichent progressivement pendant leur génération, dans une seule bulle. La fiche devient disponible une fois validée par le serveur. Le dialogue suit le texte qui arrive tant que l'on reste en bas ; remonter dans les échanges permet de les relire sans être ramené de force à la dernière ligne.

La fiche affiche les consignes, les outils et les compétences. **Créer cet agent** crée les compétences personnelles, l'agent personnel et son collègue dans le projet, en utilisant le prénom et le visage choisis dans le formulaire. **Ajuster le formulaire** permet de reprendre les réglages avant de recruter. Aucune création n'est déclenchée par les seules questions de l'entretien.

Si le recrutement du collègue échoue après la création d'une compétence ou de l'agent, une nouvelle tentative dans le même formulaire réutilise les éléments déjà créés. Les validations habituelles du serveur restent applicables.

## Historique et budget

L'entretien est une mission `Recruitment`. Les questions et réponses sont conservées en base. Le rouvrir depuis l'historique du projet affiche le dialogue RH et sa dernière fiche disponible. Le bouton **Arrêter** demande l'arrêt de la mission ; **Reprendre l'attente** permet de récupérer une réponse après une interruption de connexion sans envoyer la demande une deuxième fois.

La mission utilise le modèle par défaut et, lorsqu'elles existent, les clés personnelles de son auteur. Elle suit la file d'attente, les limites de durée, le budget mensuel, les compteurs de consommation et les garde-fous de tokens des autres missions. Ses reprises partagent le même plafond de mission. L'identifiant interne du service RH est réservé (`_rh`) et ne peut pas entrer en collision avec un agent personnalisé.

Le RH ne reçoit ni les fichiers du projet, ni les clés, ni des outils d'écriture ou d'exécution. Son seul outil prépare une fiche structurée, validée côté serveur. Un outil inventé ou une fiche invalide n'est pas proposé à la création. Il ne peut pas ajouter d'intégration qui n'existe pas dans le logiciel.

Seuls les propriétaires et éditeurs d'un projet peuvent démarrer cet entretien et recruter ; les lecteurs et personnes extérieures sont refusés avant tout appel au modèle. Les règles existantes d'accès aux missions et de réponse par leur auteur restent en vigueur.

Cette évolution n'ajoute ni table ni colonne : le résultat structuré et la conversation utilisent le stockage des missions existant. Elle s'applique aux modes local et serveur après redémarrage des composants compilés.

## Depuis le chef d’équipe

Le chef connaît le service RH intégré (`_rh`), même si aucun collègue RH n’occupe un siège. Pour une demande de recrutement, il peut utiliser `confier` pour transmettre le besoin au RH sans chercher de contacts ou de procédures dans les fichiers du projet. Le serveur crée alors un entretien RH distinct, dans le même projet et au nom de la même personne, et garde la conversation du chef intacte. **Suivre la demande avec le RH** ouvre cet entretien, ses questions et la fiche préparée. La création passe ensuite par le bouton du formulaire et les droits de recrutement habituels. Les autres agents ne disposent pas de cet outil de délégation.
