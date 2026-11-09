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
    MissionQueue queue,
    AuditLog audit,
    IAgentRunner agent,
    AgentLiveContext live,
    IProjectDirectory projects,
    ITeamDirectory team,
    IInstanceSettings instance,
    ILlmClientProvider llm,
    ILlmPricing pricing,
    IHubContext<MissionsHub> hub,
    MissionCancellations cancellations,
    IMissionWorktrees worktrees,
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

        var executionId = Guid.CreateVersion7();
        MessageAuthorDto? responseAuthor = mission.Interlocutor;
        IReadOnlyDictionary<string, AgentSeat>? identities = null;
        MessageAuthorDto Author(string role)
        {
            var seat = identities?.GetValueOrDefault(role);
            return new(role, seat?.Name ?? (role switch
            {
                "analyste" => "Analyste", "redacteur" => "Rédacteur", "developpeur" => "Développeur",
                "relecteur" => "Relecteur", "_rh" => "Agent RH", _ => role,
            }), seat?.ColleagueId, seat?.PortraitId, seat?.MouthX ?? 0.56, seat?.MouthY ?? 0.37);
        }
        AgentResume? resume = null;
        if (mission.Status == MissionStatus.AwaitingApproval && mission.Decision is null)
            return; // en pause, sans décision : rien à faire tant qu'un humain n'a pas décidé
        // L'état de reprise reste en base jusqu'à la fin de l'exécution (leçon 121) : coupée par un redémarrage,
        // elle repart exactement d'ici, avec la même décision ou la même réponse.
        if (mission.Decision is { } approved && mission.PendingApproval is not null && mission.Conversation is not null)
        {
            // Remise en file par une décision : on reprend là où l'agent s'était arrêté.
            resume = new AgentResume(mission.Conversation, approved, SpentTokens: mission.InputTokens + mission.OutputTokens);
        }
        else if (mission.FollowUp is { } followUp && mission.Conversation is { } conversation)
        {
            // Remise en file par une réponse de l'humain (leçon 117) : la conversation continue.
            resume = new AgentResume(conversation, false, followUp, mission.InputTokens + mission.OutputTokens);
        }

        // Le dossier du projet ; une mission sans projet (antérieure) garde le dossier configuré.
        string? workspace = null;
        if (mission.ProjectId is { } projectId)
        {
            var folder = await projects.WorkspaceAsync(projectId, cancellationToken);
            // Une chaîne vide interdit le repli sur le dossier global de l'administrateur,
            // tout en permettant une conversation sans outils de fichiers.
            workspace = folder.Allowed ? folder.Path : "";
        }

        // Le mode branche (leçon 127) : la première exécution crée la branche et sa copie de travail ; les suivantes
        // (décision, réponse, reprise) y retournent. Sans dépôt git, la mission échoue plutôt que de toucher la branche courante.
        if (mission.OnBranch)
        {
            if (mission.Worktree is null)
            {
                var created = workspace is null ? null : worktrees.Create(workspace, mission.Id);
                if (created is not { } branch)
                {
                    mission.Result = "Le mode branche demande que le dossier du projet soit un dépôt git avec au moins un commit.";
                    await SetStatusAsync(MissionStatus.Failed);
                    return;
                }
                (mission.Branch, mission.Worktree) = branch;
                await db.SaveChangesAsync(cancellationToken);
                await audit.AppendAsync(missionId, $"branche:{mission.Branch}", cancellationToken);
            }
            workspace = mission.Worktree;
        }

        // Le prénom, les traits et les compétences de chaque collègue du projet entrent dans sa consigne (ADR 0012, leçon 104),
        // et l'on sait qui est le chef d'équipe (leçon 129).
        var seats = identities = mission.ProjectId is { } project ? await team.SeatsAsync(project, cancellationToken) : null;
        var contactRole = mission.Kind == MissionKind.Recruitment ? "_rh" : mission.Agent ?? "analyste";
        mission.InterlocutorJson ??= AuthorJson.Write(Author(contactRole));
        responseAuthor = mission.Interlocutor;
        if (mission.Interlocutor is { ColleagueId: { } original } && seats?.GetValueOrDefault(contactRole)?.ColleagueId != original)
        {
            mission.Result = "Ce collègue ne fait plus partie de cette équipe. L’historique reste disponible ; ouvrez une conversation avec son remplaçant.";
            await SetStatusAsync(MissionStatus.Failed);
            return;
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

        mission.ExecutionId = executionId;
        await SetStatusAsync(MissionStatus.Running);
        var status = MissionStatus.Completed;
        Guid? queuedRecruitment = null;
        // La durée maximum (admin › Réglages) : au-delà, l'exécution est annulée et la mission échoue.
        var maxDuration = TimeSpan.FromMinutes(tuning.MaxDurationMinutes);
        // Le même jeton sert au bouton Arrêter (POST /missions/{id}/cancel).
        limit.CancelAfter(maxDuration);
        try
        {
            // Le direct (leçon 119) : le texte part par paquets, toutes les 150 ms au plus, vers qui peut lire le projet.
            var readers = await projects.ReadersAsync(mission.ProjectId ?? Guid.Empty, limit.Token);
            var pending = new System.Text.StringBuilder();
            var firstText = true;
            var messageId = Guid.CreateVersion7();
            long sequence = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            async Task FlushAsync()
            {
                if (pending.Length == 0)
                    return;
                var text = pending.ToString();
                pending.Clear();
                clock.Restart();
                await hub.Clients.Users(readers).SendAsync(MissionsHubProtocol.MissionText, new MissionTextDto(missionId, text, responseAuthor, messageId, ++sequence, executionId), limit.Token);
            }
            live.RoleStarted = async role =>
            {
                await FlushAsync();
                responseAuthor = Author(role);
                messageId = Guid.CreateVersion7();
                sequence = 0;
                await hub.Clients.Users(readers).SendAsync(MissionsHubProtocol.MissionActivity,
                    new MissionActivityDto(missionId, executionId, responseAuthor, "Prépare sa réponse"), limit.Token);
            };
            live.GroupContext = mission.ProjectId is { } groupProject && mission.Kind is MissionKind.Question or MissionKind.Recruitment
                ? await ProjectGroup.ContextAsync(db, groupProject, mission.Id, limit.Token) : "";
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, mission.Kind, resume, workspace, seats, mission.Agent, mission.CreatedBy,
                limit.Token, async delta =>
                {
                    pending.Append(delta);
                    if (firstText || clock.ElapsedMilliseconds >= 150)
                    {
                        firstText = false;
                        await FlushAsync();
                    }
                }, mission.SkillKeys);
            await FlushAsync();
            responseAuthor = Author(outcome.AuthorRole ?? (mission.Kind == MissionKind.Recruitment ? "_rh"
                : mission.Kind == MissionKind.Review ? "redacteur" : mission.Agent ?? "analyste"));
            mission.NeedsReply = outcome.NeedsReply;
            // Une compétence apprise et approuvée rejoint le collègue qui l'a proposée (leçon 106).
            if (mission.ProjectId is { } learnedIn)
                foreach (var learned in outcome.Learned ?? [])
                    await team.AttachSkillAsync(learnedIn, learned.Role, learned.Skill, limit.Token);
            if (outcome.Usage is { } usage)
            {
                mission.InputTokens += usage.InputTokens;
                mission.OutputTokens += usage.OutputTokens;
                mission.CostMicros += usage.CostMicros;
                mission.PersonalCostMicros += usage.PersonalCostMicros;
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
                foreach (var contribution in outcome.Contributions ?? [])
                    db.Turns.Add(new MissionTurn { MissionId = mission.Id, FromUser = false,
                        AuthorJson = AuthorJson.Write(Author(contribution.Role)), Text = contribution.Text, At = DateTimeOffset.UtcNow });
                status = MissionStatus.AwaitingApproval;
            }
            else
            {
                if (outcome.RecruitmentBrief is { Length: > 0 } brief && mission.ProjectId is { } recruitmentProject)
                {
                    var hr = new Mission
                    {
                        Id = Guid.CreateVersion7(), Goal = brief, Kind = MissionKind.Recruitment, Status = MissionStatus.Pending,
                        ProjectId = recruitmentProject, CreatedBy = mission.CreatedBy, CreatedByName = mission.CreatedByName,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.Missions.Add(hr);
                    mission.RecruitmentMissionId = hr.Id;
                    queuedRecruitment = hr.Id;
                }
                mission.Result = outcome.Answer;
                if (string.IsNullOrWhiteSpace(outcome.Answer))
                {
                    status = MissionStatus.Failed;
                    mission.Result = "Le modèle n’a renvoyé aucune réponse textuelle. Vous pouvez relancer la demande.";
                }
                // Gardée pour qu'on puisse répondre à l'agent ; trop longue, on ne la garde pas (une nouvelle mission repart à zéro).
                mission.Conversation = outcome.Conversation is { Length: <= MaxConversationChars } kept ? kept : null;
                foreach (var contribution in outcome.Contributions ?? [new AgentContribution(responseAuthor.Role, mission.RecruitmentResult?.Message ?? mission.Result ?? "")])
                    db.Turns.Add(new MissionTurn { MissionId = mission.Id, FromUser = false,
                        AuthorJson = AuthorJson.Write(Author(contribution.Role)), Text = contribution.Text, At = DateTimeOffset.UtcNow });
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
            if (ex is AgentBudgetExceededException guard)
            {
                mission.InputTokens += guard.Usage.InputTokens;
                mission.OutputTokens += guard.Usage.OutputTokens;
                mission.CostMicros += guard.Usage.CostMicros;
                mission.PersonalCostMicros += guard.Usage.PersonalCostMicros;
                await audit.AppendAsync(missionId, "budget:tokens-ou-contexte", cancellationToken);
            }
            logger.LogError(ex, "Mission {MissionId} en échec", missionId);
            status = MissionStatus.Failed;
            if (limit.IsCancellationRequested)
                mission.Result = $"Durée maximum dépassée ({maxDuration.TotalMinutes:0} min) : la mission a été arrêtée.";
            else
                mission.Result = ex switch
                {
                    // Une cause que l'humain peut corriger (aucun modèle, mode branche requis…) : dite telle quelle.
                    MissionBlockedException blocked => blocked.Message,
                    System.ClientModel.ClientResultException { Status: 429 }
                        or HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
                        "Le fournisseur du modèle a atteint une limite de requêtes ou de quota (429). " +
                        "Réessayez plus tard, ou choisissez un autre modèle dans Paramètres › Modèles. " +
                        "Si vous utilisez le mode Gratuit, ajoutez d'autres modèles gratuits à la chaîne de secours.",
                    _ => "La réponse de l'agent a échoué. La cause est dans le journal du serveur (en mode local : le fichier api.log " +
                         "du dossier de données d'Agent Yattus).",
                };
        }
        // Échouée ou arrêtée : plus rien à reprendre (on la relance, ce qui crée une nouvelle mission).
        if (status is MissionStatus.Failed or MissionStatus.Cancelled)
            (mission.Conversation, mission.FollowUp, mission.PendingApproval, mission.ApprovalId, mission.Decision) = (null, null, null, null, null);
        await SetStatusAsync(status);
        if (status == MissionStatus.Completed && queuedRecruitment is { } recruitmentId)
            await queue.Writer.WriteAsync(recruitmentId, cancellationToken);

        // Le prix du catalogue du modèle de chaque rôle, pour cette personne. La chaîne gratuite ne contient que des modèles
        // gratuits ; une clé personnelle (leçon 134) n'est pas payée par l'instance : elle ne compte pas pour son budget.
        async Task<bool> UsesPaidModelAsync(MissionKind kind, string? agentKey)
        {
            foreach (var role in AgentsModule.RolesFor(kind, agentKey, seats))
            {
                LlmClient? client;
                try
                {
                    client = await llm.GetAsync(role, mission.CreatedBy, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    continue; // mode Gratuit sans modèle : la mission le dira en partant
                }
                if (client is { Personal: false } && pricing.IsPaid(client.Model))
                    return true;
            }
            return false;
        }

        async Task SetStatusAsync(MissionStatus newStatus)
        {
            mission.Status = newStatus;
            if (mission.CreatedBy is { } recipient && newStatus is MissionStatus.Completed or MissionStatus.Failed or MissionStatus.AwaitingApproval)
                db.Notices.Add(new ConversationNotice
                {
                    Id = Guid.CreateVersion7(), MissionId = mission.Id, ProjectId = mission.ProjectId, OwnerId = recipient,
                    Turn = newStatus == MissionStatus.Completed ? db.ChangeTracker.Entries<MissionTurn>()
                        .Where(e => e.State == EntityState.Added && !e.Entity.FromUser && e.Entity.MissionId == mission.Id)
                        .Select(e => e.Entity).LastOrDefault() : null,
                    Status = newStatus, At = DateTimeOffset.UtcNow, AuthorJson = responseAuthor is null ? null : AuthorJson.Write(responseAuthor),
                    Text = newStatus == MissionStatus.AwaitingApproval ? "Votre accord est nécessaire."
                        : newStatus == MissionStatus.Failed ? "La tâche n’a pas abouti."
                        : mission.NeedsReply ? "J’ai une question pour vous." : "Ma réponse est prête.",
                });
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
