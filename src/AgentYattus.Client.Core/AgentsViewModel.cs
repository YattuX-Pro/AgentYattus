using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

/// <summary>L'écran Agents : chaque agent, son modèle, ses outils et ses limites.</summary>
public sealed partial class AgentsViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<AgentDto> Agents { get; } = [];

    [ObservableProperty]
    public partial AgentDto? Selected { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Agents.Clear();
            foreach (var agent in await api.GetAgentsAsync(cancellationToken))
                Agents.Add(agent);
            Selected = Agents.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }
}
