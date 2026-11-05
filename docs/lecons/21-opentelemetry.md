# Leçon 21 — Voir ce que font les agents : OpenTelemetry

> **Commit** : `feat(observability): OpenTelemetry`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/Telemetry.cs`, `Program.cs`, `Modules/Agents/AgentFrameworkRunner.cs`, `AgentYattus.Api.csproj`, `Properties/launchSettings.json`, `docker-compose.yml`, `tests/AgentYattus.Api.Tests/TelemetryTests.cs`

## 1. Ce qu'on construit

Pour chaque mission, on peut maintenant **voir** :

- combien de temps a duré **chaque appel au modèle** ;
- combien de **tokens** il a consommés, en entrée et en sortie ;
- comment ces appels s'**emboîtent** dans l'exécution de l'agent ;
- les requêtes HTTP reçues par l'API.

Le tout s'affiche dans un **tableau de bord** lancé avec Docker.

> **Une image** : le tableau de bord d'une voiture. Sans lui, tu sais seulement si tu es arrivé. Avec lui, tu vois la vitesse, la consommation, et le voyant qui s'allume au moment où quelque chose cloche.

## 2. Le problème

Un agent est une **boîte noire coûteuse**. Une mission a pris 40 secondes et coûté 0,30 $ : pourquoi ? Quel tour a été long ? Le modèle a-t-il relu dix fois le même fichier ? Sans mesures, on devine. Et la phase 4 (évaluer le coût par mission, par modèle) **demande** ces mesures.

## 3. Les mots de l'observabilité

| Mot | Sens | Exemple ici |
|---|---|---|
| **Trace** | Le parcours complet d'une opération | Une exécution de l'agent |
| **Span** | Une étape de ce parcours, avec un début, une fin et des étiquettes | Un appel au modèle, 1,2 s, 4 242 tokens en entrée |
| **Métrique** | Un nombre agrégé dans le temps | Tokens consommés par minute |
| **Exporteur** | Ce qui envoie ces données quelque part | Vers le tableau de bord Aspire |

**OpenTelemetry** est le **standard ouvert** pour produire ces données. On instrumente une fois, et on peut les envoyer vers n'importe quel outil compatible : Aspire, Grafana, Datadog, Azure Monitor… Il existe même des **conventions spécifiques à l'IA générative** (`gen_ai.*`), pour que tous les outils comprennent « tokens en entrée », « modèle », etc.

En .NET, un span s'appelle une **`Activity`** (dans `System.Diagnostics`).

## 4. Le code expliqué

### 4.1 Tracer les appels au modèle

```csharp
var withTools = model.AsBuilder()
    .UseFunctionInvocation(...)
    .UseOpenTelemetry(sourceName: Telemetry.SourceName, configure: otel => otel.EnableSensitiveData = false)
    .Build();
```

On ajoute une **couche** de plus autour du modèle (les poupées russes de la leçon 08). L'**ordre compte** : la télémétrie est placée **sous** la boucle d'outils. Chaque **tour** de la boucle produit donc son propre span « chat », avec sa durée et ses tokens.

### 4.2 Tracer l'agent

```csharp
return new ChatClientAgent(...)
    .AsBuilder()
    .UseOpenTelemetry(Telemetry.SourceName, otel => otel.EnableSensitiveData = false)
    .Build();
```

Un span **parent** « invoke_agent analyste » regroupe tous les appels au modèle d'une même exécution :

```
invoke_agent analyste ─────────────────────────── 3,4 s · 9 120 tokens
 ├─ chat ───────── 1,1 s · 2 980 tokens   (le modèle demande list_files)
 ├─ chat ─────────── 1,3 s · 3 050 tokens (le modèle demande read_file)
 └─ chat ─────── 1,0 s · 3 090 tokens     (le modèle répond)
```

(Exemple illustratif.)

### 4.3 `EnableSensitiveData = false` : pas de contenu

Par défaut, on n'exporte **que des métadonnées** : durées, tokens, nom du modèle. **Jamais** le but, les fichiers lus ou la réponse. C'est la même règle qu'à la leçon 19 pour les journaux : ces données partent vers des outils externes, et une réponse peut citer un secret.

### 4.4 Brancher l'ensemble — `Telemetry.cs`

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgentYattus.Api"))
    .WithTracing(tracing => tracing.AddSource(SourceName).AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddMeter(SourceName).AddAspNetCoreInstrumentation());

if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    otel.UseOtlpExporter();
```

- `AddService` : le nom de l'application dans le tableau de bord.
- `AddSource(SourceName)` : on écoute les traces des agents (source `AgentYattus`).
- `AddAspNetCoreInstrumentation` : une trace par requête HTTP reçue, sans rien coder.
- `AddMeter` : les métriques des agents, comme les tokens consommés.
- **OTLP** est le protocole standard d'envoi. On n'exporte que si une destination est configurée.

En développement, `launchSettings.json` configure la destination `http://localhost:4317` : le tableau de bord du Docker Compose.

### 4.5 Le tableau de bord

```yaml
aspire-dashboard:
  image: mcr.microsoft.com/dotnet/aspire-dashboard:9.5.2
  environment:
    DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"
  ports:
    - "127.0.0.1:18888:18888"   # l'interface
    - "127.0.0.1:4317:18889"    # la réception OTLP
```

Le tableau de bord **Aspire** de Microsoft reçoit les données OpenTelemetry et les affiche. Il tourne **sans mot de passe** : c'est acceptable uniquement parce qu'il n'écoute que sur la machine (`127.0.0.1`), comme PostgreSQL (leçon 12).

## 5. Le test

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == Telemetry.SourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activities.Enqueue,
};
ActivitySource.AddActivityListener(listener);
```

`ActivityListener`, fourni par .NET lui-même, **capte** les spans émis pendant le test. Pas besoin d'installer de paquet en plus.

Le test lance une mission dont le faux modèle déclare 4 242 tokens en entrée et 1 717 en sortie, puis vérifie :

- qu'un span « chat » porte bien ces tokens, et une durée ;
- qu'il est rattaché au span « invoke_agent analyste » ;
- que **la réponse du modèle n'apparaît dans aucune étiquette**.

On a aussi vérifié que ce dernier contrôle **sert à quelque chose** : en passant temporairement `EnableSensitiveData` à `true`, le test échoue.

Les valeurs 4 242 et 1 717 sont choisies exprès : l'écouteur capte **tous** les spans du processus, y compris ceux des autres tests qui tournent en parallèle. Des valeurs uniques permettent de retrouver les nôtres.

## 6. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests -- -class "AgentYattus.Api.Tests.TelemetryTests"
```

Avec le tableau de bord :

```bash
docker compose up -d
```

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

Lance une mission (leçon 17), puis ouvre <http://localhost:18888>, onglet **Traces**.

## 7. Quiz

1. Quelle différence entre une trace et un span ?
2. Pourquoi placer la télémétrie **sous** la boucle d'outils ?
3. Pourquoi `EnableSensitiveData = false` ?
4. Pourquoi le test utilise-t-il des nombres de tokens aussi particuliers ?
5. Pourquoi le tableau de bord peut-il tourner sans mot de passe ici ?

## 8. Pour aller plus loin

- OpenTelemetry pour .NET : <https://opentelemetry.io/docs/languages/dotnet/>
- Conventions sémantiques de l'IA générative : <https://opentelemetry.io/docs/specs/semconv/gen-ai/>
- Observabilité dans Agent Framework : <https://learn.microsoft.com/agent-framework/user-guide/agents/agent-observability>
- Le tableau de bord Aspire autonome : <https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone>

<details>
<summary>Réponses du quiz</summary>

1. Une trace est le parcours complet d'une opération ; un span est une étape de ce parcours, avec sa durée et ses étiquettes.
2. Pour obtenir un span par tour de la boucle, et voir la durée et les tokens de chaque appel au modèle.
3. Pour que ni le but, ni les fichiers lus, ni la réponse ne partent dans la télémétrie : ils peuvent contenir des secrets.
4. Parce que l'écouteur capte aussi les spans des autres tests : des valeurs uniques permettent de retrouver ceux du test.
5. Parce qu'il n'écoute que sur la machine locale. Sur un serveur, ce serait une faille.

</details>
