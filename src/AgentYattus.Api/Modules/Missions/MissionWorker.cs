using System.Text.Json;
using System.Threading.Channels;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
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

        mission.Conversation = null;
        mission.PendingApproval = null;
        mission.ApprovalId = null;
        mission.Decision = null;
        await SetStatusAsync(MissionStatus.Running);
        var status = MissionStatus.Completed;
        try
        {
            var outcome = await agent.RunAsync(mission.Id, mission.Goal, resume, cancellationToken);
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
            // ponytail: diffusion à tous les clients ; des groupes par mission quand il y aura plusieurs utilisateurs.
            await hub.Clients.All.SendAsync(
                MissionsHubProtocol.MissionEvent,
                new MissionEventDto(missionId, newStatus, DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }
}
