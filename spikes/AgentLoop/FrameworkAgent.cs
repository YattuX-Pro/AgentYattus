using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentYattus.Spikes;

/// <summary>
/// La même mission qu'<see cref="AgentLoop"/>, mais la boucle n'est plus écrite à la main :
/// <see cref="FunctionInvokingChatClient"/> exécute les outils demandés par le modèle,
/// <see cref="ChatClientAgent"/> porte l'historique, les options et le nom de l'agent.
/// </summary>
public static class FrameworkAgent
{
    public static AIAgent Create(IChatClient chatClient, IReadOnlyList<AIFunction> tools, int maxIterations = 10)
    {
        // On construit nous-mêmes le client qui exécute les outils, pour régler sa limite de tours.
        var withTools = chatClient.AsBuilder()
            .UseFunctionInvocation(configure: invoker =>
            {
                invoker.MaximumIterationsPerRequest = maxIterations;
                // Le modèle reçoit le message de l'exception (« hors du workspace »…) et peut s'adapter.
                // Nos outils écrivent des messages prévus pour lui ; ne pas activer sur des outils qui exposent des secrets.
                invoker.IncludeDetailedErrors = true;
            })
            .Build();

        return new ChatClientAgent(withTools, new ChatClientAgentOptions
        {
            Name = "explorateur",
            ChatOptions = new ChatOptions { Tools = [.. tools] },
            // Sans ce réglage, l'agent ajouterait son propre FunctionInvokingChatClient par-dessus le nôtre.
            UseProvidedChatClientAsIs = true,
        });
    }
}
