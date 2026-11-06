using System.ClientModel;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Llm;

/// <summary>
/// Le mode Gratuit : une chaîne de modèles gratuits. Quand l'un répond « quota épuisé » (ou ne répond plus),
/// on passe au suivant, et on y reste pour la suite de la mission. Jamais vers un modèle hors de la chaîne.
/// </summary>
public sealed class FallbackChatClient(IReadOnlyList<LlmClient> chain) : IChatClient
{
    private int _current;

    /// <summary>Les modèles qui ont réellement servi, dans l'ordre : le journal de la mission les affiche.</summary>
    public List<string> Used { get; } = [chain[0].Model];

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages as IList<ChatMessage> ?? [.. messages];
        while (true)
        {
            try
            {
                return await chain[_current].Client.GetResponseAsync(history, options, cancellationToken);
            }
            catch (Exception ex) when (IsExhausted(ex, cancellationToken) && Next())
            {
                // Le modèle suivant reprend la même conversation.
            }
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ponytail: bascule seulement avant le premier morceau ; les agents n'utilisent pas le streaming.
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(FallbackChatClient) ? this : chain[_current].Client.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        foreach (var link in chain)
            link.Client.Dispose();
    }

    private bool Next()
    {
        if (_current + 1 >= chain.Count)
            return false;
        _current++;
        Used.Add(chain[_current].Model);
        return true;
    }

    /// <summary>Quota épuisé (429), crédit manquant (402), service saturé (503), ou serveur injoignable.</summary>
    internal static bool IsExhausted(Exception ex, CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested && ex switch
    {
        // Status 0 : la requête n'a même pas abouti (serveur éteint, réseau coupé).
        ClientResultException { Status: 0 or 429 or 402 or 503 } => true,
        TaskCanceledException => true, // délai dépassé, puisque l'annulation demandée est exclue plus haut
        HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired or HttpStatusCode.ServiceUnavailable } => true,
        _ => false,
    };
}
