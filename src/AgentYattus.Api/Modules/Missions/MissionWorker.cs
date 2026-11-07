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
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

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
    IHostApplicationLifetime lifetime,
    ILogger<MissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // La file est en mémoire : au démarrage, on y remet ce qu'un arrêt a interrompu (leçon 121).
        // Après le démarrage complet : les tables existent (SchemaUpdater) quel que soit l'ordre des services.
        var started = new TaskCompletionSource();
        await using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        await using (stoppingToken.Register(() => started.TrySetCanceled()))
            await started.Task;
        await RequeueInterruptedAsync(stoppingToken);

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

    /// <summary>
    /// En file (« Pending ») : remise en file. En cours (« Running ») : l'exécution a été coupée ; elle repart du dernier
    /// état enregistré (le but, la décision ou la réponse en attente), notée « reprise » dans le journal.
    /// Une décision prise mais pas encore traitée (pause avec Decision) repart aussi.
    /// </summary>
    private async Task RequeueInterruptedAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MissionsDb>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        var missions = await db.Missions
            .Where(m => m.Status == MissionStatus.Pending || m.Status == MissionStatus.Running
                        || (m.Status == MissionStatus.AwaitingApproval && m.Decision != null))
            .ToListAsync(stoppingToken);
        foreach (var mission in missions.OrderBy(m => m.CreatedAt.UtcTicks))
        {
            if (mission.Status == MissionStatus.Running)
            {
                mission.Status = MissionStatus.Pending;
                await db.SaveChangesAsync(stoppingToken);
                await audit.AppendAsync(mission.Id, "reprise", stoppingToken);
            }
            await queue.Writer.WriteAsync(mission.Id, stoppingToken);
        }
        if (missions.Count > 0)
            logger.LogInformation("{Count} missions remises en file après le redémarrage", missions.Count);
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
internal sealed partial class MissionRunner
{
    /// <summary>Au-delà (environ 250 000 tokens), une mission terminée ne garde pas sa conversation : on n'y répond plus.</summary>
    public const int MaxConversationChars = 1_000_000;
}

internal sealed partial class MissionRunner(
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
        if (mission.Status == MissionStatus.AwaitingApproval && mission.Decision is null)
            return; // en pause, sans décision : rien à faire tant qu'un humain n'a pas décidé
        // L'état de reprise reste en base jusqu'à la fin de l'exécution (leçon 121) : coupée par un redémarrage,
        // elle repart exactement d'ici, avec la même décision ou la même réponse.
        if (mission.Decision is { } approved && mission.PendingApproval is not null && mission.Conversation is not null)
        {
            // Remise en file par une décision : on reprend là où l'agent s'était arrêté.
            resume = new AgentResume(mission.Conversation, approved);
        }
        else if (mission.FollowUp is { } followUp && mission.Conversation is { } conversation)
        {
            // Remise en file par une réponse de l'humain (leçon 117) : la conversation continue.
            resume = new AgentResume(conversation, false, followUp);
        }

        // Le dossier du projet ; une mission sans projet (antérieure) garde le dossier configuré.
        string? workspace = null;
        if (mission.ProjectId is { } projectId)
        {
            var folder = await projects.WorkspaceAsync(projectId, cancellationToken);
            if (!folder.Allowed)
            {
                mission.Result = "Ce projet n'a pas encore de dossier de travail : choisis-en un depuis l'accueil (bouton dossier), ou demande-le à l'administrateur.";
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
            // Le direct (leçon 119) : le texte part par paquets, toutes les 150 ms au plus, vers qui peut lire le projet.
            var readers = await projects.ReadersAsync(mission.ProjectId ?? Guid.Empty, limit.Token);
            var pending = new System.Text.StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            async Task FlushAsync()
            {
                if (pending.Length == 0)
                    return;
                var text = pending.ToString();
                pending.Clear();
                clock.Restart();
                await hub.Clients.Users(readers).SendAsync(MissionsHubProtocol.MissionText, new MissionTextDto(missionId, text), limit.Token);
            }
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, mission.Kind, resume, workspace, seats, mission.Agent, mission.CreatedBy,
                limit.Token, async delta =>
                {
                    pending.Append(delta);
                    if (clock.ElapsedMilliseconds >= 150)
                        await FlushAsync();
                });
            await FlushAsync();
            // Une compétence apprise et approuvée rejoint le collègue qui l'a proposée (leçon 106).
            if (mission.ProjectId is { } learnedIn)
                foreach (var learned in outcome.Learned ?? [])
                    await team.AttachSkillAsync(learnedIn, learned.Role, learned.Skill, limit.Token);
            if (outcome.Usage is { } usage)
            {
                mission.InputTokens += usage.InputTokens;
                mission.OutputTokens += usage.OutputTokens;
                mission.CostMicros += usage.CostMicros;
            }
            // L'exécution est allée au bout : l'ancien état de reprise ne sert plus.
            (mission.Conversation, mission.FollowUp, mission.PendingApproval, mission.ApprovalId, mission.Decision) = (null, null, null, null, null);
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
                // Gardée pour qu'on puisse répondre à l'agent ; trop longue, on ne la garde pas (une nouvelle mission repart à zéro).
                mission.Conversation = outcome.Conversation is { Length: <= MaxConversationChars } kept ? kept : null;
                db.Turns.Add(new MissionTurn { MissionId = mission.Id, FromUser = false, Text = outcome.Answer ?? "", At = DateTimeOffset.UtcNow });
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
        // Échouée ou arrêtée : plus rien à reprendre (on la relance, ce qui crée une nouvelle mission).
        if (status is MissionStatus.Failed or MissionStatus.Cancelled)
            (mission.Conversation, mission.FollowUp, mission.PendingApproval, mission.ApprovalId, mission.Decision) = (null, null, null, null, null);
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
