using AgentYattus.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgentYattus.Client.Core;

public sealed class MissionsHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    // handlerFactory sert aux tests (TestServer) ; null en usage normal.
    // accessTokenProvider : le jeton de la session, que le client SignalR passe au hub.
    public MissionsHubClient(
        Uri apiBaseAddress, Func<HttpMessageHandler>? handlerFactory = null, Func<Task<string?>>? accessTokenProvider = null)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBaseAddress, MissionsHubProtocol.Path), options =>
            {
                if (handlerFactory is not null)
                    options.HttpMessageHandlerFactory = _ => handlerFactory();
                if (accessTokenProvider is not null)
                    options.AccessTokenProvider = accessTokenProvider;
            })
            .WithAutomaticReconnect()
            .Build();
        _connection.On<MissionEventDto>(MissionsHubProtocol.MissionEvent, e => MissionEventReceived?.Invoke(e));
    }

    public event Action<MissionEventDto>? MissionEventReceived;

    public Task StartAsync(CancellationToken cancellationToken = default) => _connection.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => _connection.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
