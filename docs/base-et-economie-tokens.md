# Choisir la base et limiter les tokens

Le bureau démarre automatiquement sur le serveur, sans écran de choix. Un ancien choix local dans `connection.json` ne change pas ce comportement. Les adresses enregistrées sont réutilisées ; les variables `AGENTYATTUS_API_URL`, `AGENTYATTUS_AUTHORITY` et `AGENTYATTUS_ADMIN_URL` ont priorité. La base PostgreSQL est celle configurée par le serveur.

Le mode local reste accessible uniquement par configuration explicite (`AGENTYATTUS_MODE=local`). Il ouvre `agent-yattus.db` dans le dossier de données utilisateur ; `Local__DataFolder` reste respecté. Aucune migration ni bascule automatique vers une autre base n’est effectuée si le serveur est indisponible. Le mode actif reste visible en bas de la fenêtre.

Le volume Docker PostgreSQL existant n'est pas modifié par cette évolution. La persistance de Keycloak et les sauvegardes relèvent d'un chantier distinct.

## Économies de contexte

- `read_file` et `read_documentation` acceptent `startLine` (à partir de 1) et `lineCount` (120 par défaut, 500 au maximum). Les extraits indiquent la ligne de continuation. Les petits fichiers restent rendus tels quels.
- Un extrait de fichier contient au plus 10 000 caractères de corps ; une page de documentation, 8 000. Une ligne qui dépasse à elle seule cette taille est explicitement marquée comme tronquée.
- Les pages déjà téléchargées sont réutilisées pour lire la suite pendant la même exécution, avec leur date, leur empreinte et leurs contrôles d'accès d'origine.
- Les compétences sélectionnées sont présentées par leur clé, version et description. Le modèle charge leur procédure avec `use_skill`. Les règles de priorité et les restrictions de visibilité sont conservées. Une procédure chargée n'est pas abrégée par le garde-fou de contexte.
- Chaque requête au fournisseur passe par le même garde-fou, y compris les tours d'outils, les réponses en flux et les reprises. Il travaille sur une copie : aucune réécriture de l'historique conservé ni des arguments d'actions à approuver.
- Les grands résultats d'outils sont abrégés et signalés. Au-delà du seuil de condensation, les résultats les plus anciens sont raccourcis davantage ; les demandes humaines et les décisions ne sont jamais tronquées. Ce mécanisme extrait du texte : il ne produit pas de résumé par un autre modèle et ne facture donc aucun appel supplémentaire.
- Si le contexte reste trop grand, la mission s'arrête avec un message explicatif avant le prochain appel au fournisseur.

## Limites de départ

Dans `src/AgentYattus.Api/appsettings.json`, section `Agents:TokenEconomy` :

| Réglage | Valeur | Effet |
| --- | ---: | --- |
| `MaxOutputTokens` | 4 096 | Plafond de sortie de chaque appel au modèle |
| `MaxMissionTokens` | 100 000 | Budget partagé par les rôles, incluant les reprises et réponses à la même mission |
| `MaxContextChars` | 120 000 | Taille maximale du contexte sérialisé, consignes et descriptions d'outils comprises |
| `CompactAfterChars` | 60 000 | Seuil à partir duquel les anciens résultats sont davantage abrégés |
| `MaxToolResultChars` | 12 000 | Taille maximale d'un résultat d'outil envoyé au modèle, hors procédures de compétences |
| `OldToolResultChars` | 800 | Taille des extraits des anciens résultats lorsque le seuil est atteint |
| `RecentToolResults` | 4 | Nombre de résultats récents conservant le plafond normal |

Les valeurs incohérentes empêchent le démarrage avec une erreur de configuration. Ces limites sont configurables aussi par variables d'environnement, par exemple `Agents__TokenEconomy__MaxOutputTokens`. Elles ne nécessitent aucune migration de base.

Le budget de mission utilise les compteurs remontés par le fournisseur et se vérifie **entre les appels** : un appel en cours peut dépasser le solde, notamment par ses tokens d'entrée. Si le fournisseur ne fournit pas de consommation, le plafond total n'est pas mesurable ; les plafonds de contexte, de sortie et de tours restent applicables. La consommation et le coût déjà remontés sont conservés lorsqu'un garde-fou arrête la mission. Les limites en caractères ne sont pas un comptage exact des tokens.

Le choix des modèles par rôle et les contrôles de développement existants sont conservés. Les gains devront être mesurés en tokens et coût par mission réussie sur les évaluations du projet, avant de réduire davantage les plafonds.

Le mode Gratuit transmet aussi les réponses en streaming. Une bascule de secours peut avoir lieu avant le premier morceau transmis ; après le début du flux, une erreur est signalée au lieu de mélanger la réponse d'un autre modèle. Aucun appel supplémentaire n'est nécessaire pour afficher le texte progressivement.

## Mode Gratuit personnel

Sur le serveur, « Mes clés » possède son propre interrupteur Gratuit, sans rôle administrateur. Il utilise automatiquement les modèles marqués gratuits au catalogue parmi les connexions personnelles actives, répartit jusqu’à dix essais entre les fournisseurs et bascule en cas de quota épuisé. Connecter un fournisseur supplémentaire l’inclut automatiquement. Le réglage persiste par utilisateur dans `llm.UserPreferences` et ne change pas les réglages communs du serveur. Aucun modèle payant, aucune clé d’un autre utilisateur ni clé commune ne sert de repli quand ce mode est actif. Sans modèle gratuit disponible, une erreur explicite est renvoyée.

Plusieurs fournisseurs peuvent rester connectés simultanément. Leur ajout conserve les connexions existantes et ne force plus à choisir immédiatement un modèle personnel. Hors mode Gratuit, le choix manuel reste disponible avec « Changer ».

En streaming Gratuit, chaque modèle dispose de vingt secondes pour envoyer sa première mise à jour. Sans réponse, l’appel est annulé et le modèle gratuit suivant est essayé. Dès le début du flux, cette limite est retirée pour permettre de terminer la réponse ; une annulation de l’utilisateur arrête la chaîne. Cela limite l’attente d’un fournisseur silencieux, sans garantir le délai total de génération ni la disponibilité de la chaîne.
