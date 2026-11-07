using System.Text.Json;
using System.Threading.Channels;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Api.Modules.Team;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace AgentYattus.Api.Modules.Missions;

// ponytail: file en mémoire ; au redémarrage, une mission en attente est perdue et une mission
// en cours reste « Running ». La reprise après redémarrage est prévue après le MVP.
/// <summary>
/// Les missions en cours d'exécution et de quoi les arrêter. Un arrêt demandé par un humain est noté,
/// pour que le runner termine la mission en « Arrêtée » plutôt qu'en échec.
/// </summary>
internal sealed class MissionCancellations
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (CancellationTokenSource Source, bool ByUser)> _running = new();

    public void Register(Guid missionId, CancellationTokenSource source) => _running[missionId] = (source, false);

    public void Remove(Guid missionId) => _running.TryRemove(missionId, out _);

    /// <summary>Vrai si la mission tournait : son exécution est annulée.</summary>
    public bool Cancel(Guid missionId)
    {
        if (!_running.TryGetValue(missionId, out var entry))
            return false;
        _running[missionId] = (entry.Source, true);
        entry.Source.Cancel();
        return true;
    }

    public bool CancelledByUser(Guid missionId) => _running.TryGetValue(missionId, out var entry) && entry.ByUser;
}

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
    ITeamDirectory team,
    IInstanceSettings instance,
    ILlmClientProvider llm,
    ILlmPricing pricing,
    IHubContext<MissionsHub> hub,
    MissionCancellations cancellations,
    ILogger<MissionRunner> logger)
{
    public async Task RunAsync(Guid missionId, CancellationToken cancellationToken)
    {
        // Le jeton de la mission est enregistré avant toute chose : un arrêt demandé pendant le démarrage n'est pas perdu.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellations.Register(missionId, limit);
        try
        {
            await RunCoreAsync(missionId, limit, cancellationToken);
        }
        finally
        {
            cancellations.Remove(missionId);
        }
    }

    private async Task RunCoreAsync(Guid missionId, CancellationTokenSource limit, CancellationToken cancellationToken)
    {
        var mission = await db.Missions.FindAsync([missionId], cancellationToken);
        // Arrêtée pendant qu'elle attendait son tour : elle ne part pas.
        if (mission is null or { Status: MissionStatus.Cancelled })
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
            && await UsesPaidModelAsync(mission.Kind, mission.Agent))
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
        // Le même jeton sert au bouton Arrêter (POST /missions/{id}/cancel).
        limit.CancelAfter(maxDuration);
        try
        {
            // Le prénom, les traits et les compétences de chaque collègue du projet entrent dans sa consigne (ADR 0012, leçon 104).
            var seats = mission.ProjectId is { } project ? await team.SeatsAsync(project, limit.Token) : null;
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, mission.Kind, resume, workspace, seats, mission.Agent, limit.Token);
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested && cancellations.CancelledByUser(missionId))
        {
            status = MissionStatus.Cancelled;
            mission.Result = "Arrêtée à la demande d'un humain.";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Mission {MissionId} en échec", missionId);
            status = MissionStatus.Failed;
            if (limit.IsCancellationRequested)
                mission.Result = $"Durée maximum dépassée ({maxDuration.TotalMinutes:0} min) : la mission a été arrêtée.";
        }
        await SetStatusAsync(status);

        // « gratuit : … » : la chaîne gratuite ; sinon le prix du catalogue du modèle de chaque rôle.
        async Task<bool> UsesPaidModelAsync(MissionKind kind, string? agentKey)
        {
            foreach (var role in AgentsModule.RolesFor(kind, agentKey))
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
