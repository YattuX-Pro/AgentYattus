# ADR 0007 — Plusieurs fournisseurs de modèles et le catalogue models.dev

- **Statut** : proposé
- **Date** : 2026-11-06

## Contexte

L'ADR 0006 a mis la gestion du modèle dans l'application, mais pour Anthropic seulement, avec trois modèles écrits dans le code. On veut faire comme [opencode](https://opencode.ai) : **connecter n'importe quel fournisseur** (clé d'API, serveur local, adresse compatible OpenAI), puis choisir un modèle dans **un seul sélecteur**, avec recherche, contexte et prix.

## Décision

1. **Deux protocoles suffisent.** L'API d'Anthropic (SDK `Anthropic`) et celle d'OpenAI (`Microsoft.Extensions.AI.OpenAI`), que presque tous les fournisseurs imitent : OpenAI, Google Gemini, Mistral, Groq, DeepSeek, xAI, OpenRouter, OpenCode Zen, Ollama, LM Studio. Les deux donnent un `IChatClient` : les agents ne voient pas la différence.
2. **Trois sortes de connexions** : à **clé d'API** (adresse imposée), **locale** (Ollama, LM Studio : adresse proposée, clé facultative), **personnalisée** (« Compatible OpenAI » : adresse obligatoire).
3. **Le catalogue vient de [models.dev](https://models.dev)**, comme dans opencode : noms, contexte, prix, raisonnement. On ne garde que les modèles **capables d'appeler des outils** : un agent sans outils ne peut rien faire. Une copie filtrée est **embarquée** dans l'API (`scripts/update-models-snapshot.py`) ; le service `CatalogRefresher` la remplace par la version en ligne au démarrage, puis chaque jour. Hors ligne, la copie suffit.
4. **Les serveurs locaux annoncent leurs modèles** : `GET {adresse}/models`, le standard OpenAI. On ne connaît ni leur prix ni leur contexte ; ils sont gratuits.
5. **Un choix = une connexion + un modèle** (`LlmSelectionDto`), parce qu'on peut avoir deux clés chez le même fournisseur.
6. **Garde-fous sur les adresses** (la clé y est envoyée) : HTTPS obligatoire, sauf `localhost` ; ni identifiants ni paramètres dans l'adresse ; l'adresse d'un fournisseur à clé est **imposée**, pour qu'une clé Anthropic ne parte jamais ailleurs ; adresses link-local refusées (169.254.x.x, fe80::, où les clouds servent leurs métadonnées) ; aucune redirection suivie ; réponse d'un serveur local limitée à 1 Mo.
7. **Ce qui vient de l'extérieur est une donnée non fiable** : identifiants filtrés (caractères sûrs, 200 au plus), noms nettoyés, nombre de modèles par serveur borné ; la forme du JSON est vérifiée à chaque niveau, et une erreur de mise à jour du catalogue n'arrête jamais l'API.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Un adaptateur par fournisseur | Toutes les fonctions propres à chacun | Dix paquets, dix façons de casser |
| **Anthropic + compatible OpenAI** | Deux adaptateurs couvrent tout | Certaines fonctions propres à un fournisseur restent inaccessibles |
| Catalogue écrit à la main | Aucune dépendance | Les prix et les modèles changent chaque mois |
| **models.dev en ligne + copie embarquée** | À jour, et fonctionne hors ligne | Un appel sortant par jour ; une donnée externe à valider |
| Abonnements par OAuth (ChatGPT, Copilot, Claude Pro) | Pas de clé à gérer | Interdit par Anthropic pour Claude ; propre à chaque éditeur |

## Conséquences

- (+) Changer de fournisseur ou de modèle ne demande ni code ni redémarrage.
- (+) Modèles locaux : gratuits, et le code ne quitte pas la machine.
- (−) L'API fait un appel sortant par jour vers models.dev (désactivable : `Llm:CatalogUrl` vide).
- (−) Un administrateur peut faire appeler par le serveur une adresse HTTPS de son choix, y compris du **réseau privé** (une passerelle d'entreprise, par exemple). Les adresses link-local sont refusées **à la connexion**, après la résolution DNS (`SocketsHttpHandler.ConnectCallback`), ce qui bloque aussi le *DNS rebinding*. C'est acceptable tant que seul `llm-admin` peut le faire ; pour un déploiement serveur : une liste d'adresses autorisées.
- (−) Un serveur local du réseau (HTTP hors `localhost`) est refusé pour l'instant.
- Les tables gardent leurs noms de l'ADR 0006 (`Credentials`, `ActiveCredentialId`) : seul le code parle de « connexions ».

## Sources

- opencode, fournisseurs : <https://opencode.ai/docs/providers/>
- models.dev : <https://models.dev>
- Microsoft.Extensions.AI.OpenAI : <https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai>
- API compatible OpenAI de Gemini : <https://ai.google.dev/gemini-api/docs/openai>
- API compatible OpenAI d'Ollama : <https://github.com/ollama/ollama/blob/main/docs/openai.md>
