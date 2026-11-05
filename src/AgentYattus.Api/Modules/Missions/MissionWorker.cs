using System.Threading.Channels;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Modules.Missions;

// ponytail: file en mémoire ; au redémarrage, une mission en attente est perdue et une mission
// en cours reste « Running ». La reprise après redémarrage est prévue après le MVP.
internal sealed class MissionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}

// ponytail: une seule mission à la fois. Avant l'étape Approbations, une mission en pause doit
// quitter le worker (état persisté, remise en file à l'approbation) au lieu d'attendre ici.
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
    ILogger<MissionRunner> logger)
{
    public async Task RunAsync(Guid missionId, CancellationToken cancellationToken)
    {
        var mission = await db.Missions.FindAsync([missionId], cancellationToken);
        if (mission is null)
            return;

        await SetStatusAsync(MissionStatus.Running);
        var status = MissionStatus.Completed;
        try
        {
            await agent.RunAsync(mission.Id, mission.Goal, cancellationToken);
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
        }
    }
}
