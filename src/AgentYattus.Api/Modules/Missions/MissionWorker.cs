using System.Text.Json;
using System.Threading.Channels;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace AgentYattus.Api.Modules.Missions;

// ponytail: file en mémoire ; au redémarrage, une mission en attente est perdue et une mission
// en cours reste « Running ». La reprise après redémarrage est prévue après le MVP.
internal sealed class MissionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}

// Serveur vers client uniquement : aucune méthode appelable.
internal sealed class MissionsHub : Hub;

// Plusieurs missions à la fois, autant que le réglage « missions simultanées » (ADR 0010), relu avant chaque départ.
// Une mission en attente d'approbation n'occupe pas de place : son état est en base, et la décision la remet dans la file.
internal sealed class MissionWorker(
    MissionQueue queue,
    IServiceScopeFactory scopes,
    ILogger<MissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new List<Task>();
        await foreach (var missionId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            running.RemoveAll(t => t.IsCompleted);
            var max = await MaxConcurrentAsync(stoppingToken);
            while (running.Count >= max)
            {
                await Task.WhenAny(running);
                running.RemoveAll(t => t.IsCompleted);
            }
            running.Add(RunOneAsync(missionId, stoppingToken));
        }
        await Task.WhenAll(running);
    }

    private async Task<int> MaxConcurrentAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return Math.Max(1, (await scope.ServiceProvider.GetRequiredService<IInstanceSettings>().GetAsync(stoppingToken)).MaxConcurrentMissions);
    }

    private async Task RunOneAsync(Guid missionId, CancellationToken stoppingToken)
    {
        await Task.Yield(); // rend la main à la boucle : la mission suivante peut être lue pendant que celle-ci tourne
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MissionRunner>().RunAsync(missionId, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Une mission en échec ne doit pas arrêter le worker (ni l'hôte).
            logger.LogError(ex, "Mission {MissionId} interrompue", missionId);
        }
    }
}

// Dépendances inter-modules en paramètres du constructeur : le test d'architecture les voit.
internal sealed class MissionRunner(
    MissionsDb db,
    AuditLog audit,
    IAgentRunner agent,
    IProjectDirectory projects,
    IInstanceSettings instance,
    ILlmClientProvider llm,
    ILlmPricing pricing,
    IHubContext<MissionsHub> hub,
    ILogger<MissionRunner> logger)
{
    public async Task RunAsync(Guid missionId, CancellationToken cancellationToken)
    {
        var mission = await db.Missions.FindAsync([missionId], cancellationToken);
        if (mission is null)
            return;

        AgentResume? resume = null;
        if (mission.Status == MissionStatus.AwaitingApproval)
        {
            // Remise en file par une décision : on reprend là où l'agent s'était arrêté.
            if (mission.Decision is not { } approved || mission.Conversation is null)
                return;
            resume = new AgentResume(mission.Conversation, approved);
        }

        // Le dossier du projet ; une mission sans projet (antérieure) garde le dossier configuré.
        string? workspace = null;
        if (mission.ProjectId is { } projectId)
        {
            var folder = await projects.WorkspaceAsync(projectId, cancellationToken);
            if (!folder.Allowed)
            {
                mission.Result = "Ce projet n'a pas encore de dossier de travail : un administrateur de l'instance doit en choisir un (espace admin › Projets).";
                await SetStatusAsync(MissionStatus.Failed);
                return;
            }
            workspace = folder.Path;
        }

        // Le budget (ADR 0010) : au plafond, une mission qui utiliserait un modèle payant est refusée ; le gratuit passe.
        var tuning = await instance.GetAsync(cancellationToken);
        if (tuning is { BlockAtBudget: true, MonthlyBudget: { } budget }
            && await MissionsModule.SpentThisMonthMicrosAsync(db, cancellationToken) >= budget * 1_000_000m
            && await UsesPaidModelAsync(mission.Kind))
        {
            await audit.AppendAsync(missionId, "budget:atteint", cancellationToken);
            mission.Result = $"Budget du mois atteint ({budget:0.##} $) : les modèles payants sont suspendus jusqu'au mois prochain. " +
                "Un administrateur peut relever le plafond (espace admin › Réglages) ; les modèles gratuits restent disponibles.";
            await SetStatusAsync(MissionStatus.Failed);
            return;
        }

        mission.Conversation = null;
        mission.PendingApproval = null;
        mission.ApprovalId = null;
        mission.Decision = null;
        await SetStatusAsync(MissionStatus.Running);
        var status = MissionStatus.Completed;
        // La durée maximum (admin › Réglages) : au-delà, l'exécution est annulée et la mission échoue.
        var maxDuration = TimeSpan.FromMinutes(tuning.MaxDurationMinutes);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(maxDuration);
        try
        {
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, mission.Kind, resume, workspace, limit.Token);
            if (outcome.Usage is { } usage)
            {
                mission.InputTokens += usage.InputTokens;
                mission.OutputTokens += usage.OutputTokens;
                mission.CostMicros += usage.CostMicros;
            }
            if (outcome.Approval is { } approval)
            {
                // Pause : on enregistre de quoi reprendre, et le worker passe à la mission suivante.
                mission.Conversation = outcome.Conversation;
                // Chaque pause a son identifiant : une décision prise sur une demande précédente ne peut pas valoir pour celle-ci.
                mission.ApprovalId = Guid.CreateVersion7();
                mission.PendingApproval = JsonSerializer.Serialize(
                    new ApprovalDto(mission.ApprovalId.Value, [.. approval.ToolCalls.Select(c => new ToolCallDto(c.Tool, c.Arguments))]),
                    JsonSerializerOptions.Web);
                status = MissionStatus.AwaitingApproval;
            }
            else
            {
                mission.Result = outcome.Answer;
            }
        }
        // Filtre sur le jeton, pas sur le type : un délai HTTP dépassé dans le runner est un échec.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Mission {MissionId} en échec", missionId);
            status = MissionStatus.Failed;
            if (limit.IsCancellationRequested)
                mission.Result = $"Durée maximum dépassée ({maxDuration.TotalMinutes:0} min) : la mission a été arrêtée.";
        }
        await SetStatusAsync(status);

        // « gratuit : … » : la chaîne gratuite ; sinon le prix du catalogue du modèle de chaque rôle.
        async Task<bool> UsesPaidModelAsync(MissionKind kind)
        {
            foreach (var role in AgentsModule.RolesFor(kind))
            {
                var model = await llm.GetModelAsync(role, cancellationToken);
                if (!model.StartsWith("gratuit", StringComparison.Ordinal) && pricing.IsPaid(model))
                    return true;
            }
            return false;
        }

        async Task SetStatusAsync(MissionStatus newStatus)
        {
            mission.Status = newStatus;
            await db.SaveChangesAsync(cancellationToken);
            await audit.AppendAsync(missionId, newStatus.ToString(), cancellationToken);
            // Le direct suit les droits : seules les personnes qui peuvent lire le projet reçoivent l'événement.
            // Une mission sans projet (antérieure) : les seuls administrateurs de l'instance.
            var readers = await projects.ReadersAsync(mission.ProjectId ?? Guid.Empty, cancellationToken);
            await hub.Clients.Users(readers).SendAsync(
                MissionsHubProtocol.MissionEvent,
                new MissionEventDto(missionId, newStatus, DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }
}
