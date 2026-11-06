using System.Text.Json;
using System.Threading.Channels;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Projects;
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

// ponytail: une seule mission à la fois. Une mission en attente d'approbation ne bloque pas le worker :
// son état est en base, et la décision la remet dans la file.
internal sealed class MissionWorker(
    MissionQueue queue,
    IServiceScopeFactory scopes,
    ILogger<MissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var missionId in queue.Reader.ReadAllAsync(stoppingToken))
        {
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
}

// Dépendances inter-modules en paramètres du constructeur : le test d'architecture les voit.
internal sealed class MissionRunner(
    MissionsDb db,
    AuditLog audit,
    IAgentRunner agent,
    IProjectDirectory projects,
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

        mission.Conversation = null;
        mission.PendingApproval = null;
        mission.ApprovalId = null;
        mission.Decision = null;
        await SetStatusAsync(MissionStatus.Running);
        var status = MissionStatus.Completed;
        try
        {
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, mission.Kind, resume, workspace, cancellationToken);
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
        }
        await SetStatusAsync(status);

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
