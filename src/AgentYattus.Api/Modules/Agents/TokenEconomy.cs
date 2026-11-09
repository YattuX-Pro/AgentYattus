using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Agents;

internal sealed class TokenEconomyOptions
{
    public int MaxOutputTokens { get; set; } = 4_096;
    public long MaxMissionTokens { get; set; } = 100_000;
    public int MaxContextChars { get; set; } = 120_000;
    public int CompactAfterChars { get; set; } = 60_000;
    public int MaxToolResultChars { get; set; } = 12_000;
    public int OldToolResultChars { get; set; } = 800;
    public int RecentToolResults { get; set; } = 4;

    public bool IsValid => MaxOutputTokens is >= 256 and <= 32_768
        && MaxMissionTokens is >= 1_000 and <= 10_000_000
        && MaxContextChars is >= 16_000 and <= 2_000_000
        && CompactAfterChars >= 8_000 && CompactAfterChars <= MaxContextChars
        && MaxToolResultChars >= 1_000 && MaxToolResultChars <= MaxContextChars
        && OldToolResultChars is >= 200 && OldToolResultChars <= MaxToolResultChars
        && RecentToolResults is >= 1 and <= 20;
}

/// <summary>Partagé par tous les rôles d'une exécution ; les reprises incluent la consommation déjà stockée.</summary>
internal sealed class MissionTokenBudget(long maximum)
{
    public long SpentTokens { get; set; }
    public AgentUsage Usage { get; private set; } = new(0, 0, 0);
    public long Remaining => Math.Max(0, maximum - SpentTokens);

    public void Reset(long previous)
    {
        SpentTokens = previous;
        Usage = new AgentUsage(0, 0, 0);
    }

    public void Observe(UsageDetails? usage, Func<long, long, AgentUsage>? price)
    {
        if (usage is null)
            return;
        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;
        SpentTokens += Math.Max(usage.TotalTokenCount ?? 0, input + output);
        Usage += price?.Invoke(input, output) ?? new AgentUsage(input, output, 0);
    }
}

/// <summary>
/// Sous la boucle d'outils : chaque requête (y compris une reprise) est bornée, en flux comme sans flux.
/// Seule une copie envoyée au modèle est allégée ; l'historique stocké et les décisions restent intacts.
/// Les caractères sont une limite de volume, pas un comptage exact des tokens du fournisseur.
/// </summary>
internal sealed class TokenEconomyChatClient(
    IChatClient inner, TokenEconomyOptions limits, MissionTokenBudget? budget = null,
    Func<long, long, AgentUsage>? price = null) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var bounded = Prepare(messages, options);
        var response = await InnerClient.GetResponseAsync(bounded.Messages, bounded.Options, cancellationToken);
        budget?.Observe(response.Usage, price);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var bounded = Prepare(messages, options);
        await foreach (var update in InnerClient.GetStreamingResponseAsync(bounded.Messages, bounded.Options, cancellationToken))
        {
            foreach (var usage in update.Contents.OfType<UsageContent>())
                budget?.Observe(usage.Details, price);
            yield return update;
        }
    }

    internal (List<ChatMessage> Messages, ChatOptions Options) Prepare(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var requestOptions = options?.Clone() ?? new ChatOptions();
        requestOptions.MaxOutputTokens = Math.Min(requestOptions.MaxOutputTokens ?? limits.MaxOutputTokens, limits.MaxOutputTokens);
        if (budget is not null)
        {
            if (budget.Remaining < 256)
                throw new AgentBudgetExceededException(
                    "Le budget de tokens de cette mission est atteint. Créez une nouvelle mission plus ciblée, " +
                    "ou demandez à l'administrateur d'ajuster Agents:TokenEconomy:MaxMissionTokens.", budget.Usage);
            requestOptions.MaxOutputTokens = (int)Math.Min(requestOptions.MaxOutputTokens.Value, budget.Remaining);
        }
        var history = messages.Select(m =>
        {
            var copy = m.Clone();
            copy.Contents = [.. m.Contents];
            return copy;
        }).ToList();
        var overhead = (requestOptions.Instructions?.Length ?? 0)
            + (requestOptions.Tools?.OfType<AIFunction>().Sum(t => t.JsonSchema.GetRawText().Length + t.Description.Length) ?? 0);
        var compact = Size(history) + overhead > limits.CompactAfterChars;
        var results = history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        var old = results.Take(Math.Max(0, results.Count - limits.RecentToolResults)).ToHashSet();
        // Une procédure chargée doit rester entière : la tronquer pourrait retirer une règle essentielle.
        var skillCalls = history.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
            .Where(c => c.Name == AgentRoles.UseSkill).Select(c => c.CallId).ToHashSet();
        foreach (var message in history)
            for (var i = 0; i < message.Contents.Count; i++)
                if (message.Contents[i] is FunctionResultContent result && !skillCalls.Contains(result.CallId))
                {
                    var text = result.Result is string value ? value : JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions);
                    var max = compact && old.Contains(result) ? limits.OldToolResultChars : limits.MaxToolResultChars;
                    if (text.Length > max)
                        message.Contents[i] = new FunctionResultContent(result.CallId,
                            Abbreviate(text, max));
                }
        if (Size(history) + overhead > limits.MaxContextChars)
            throw new AgentBudgetExceededException(
                "La limite de contexte est atteinte. Ouvrez une nouvelle mission avec un objectif ciblé et les décisions utiles ; " +
                "les échanges affichés restent dans l’historique. Un administrateur peut ajuster Agents:TokenEconomy:MaxContextChars.",
                budget?.Usage ?? new AgentUsage(0, 0, 0));
        return (history, requestOptions);
    }

    private static int Size(List<ChatMessage> messages) => JsonSerializer.Serialize(messages, AIJsonUtilities.DefaultOptions).Length;

    private static string Abbreviate(string text, int maximum)
    {
        var head = maximum * 2 / 3;
        var tail = text.Length - (maximum - head);
        if (char.IsHighSurrogate(text[head - 1])) head--;
        if (char.IsLowSurrogate(text[tail])) tail++;
        // La fin conserve notamment les erreurs des commandes, souvent écrites après les journaux.
        return text[..head] + "\n[Extrait : contenu intermédiaire omis. Relis un passage ciblé si nécessaire.]\n" + text[tail..];
    }
}

internal static class TextExcerpt
{
    public static string Read(string text, int startLine, int lineCount, int maxChars, string source)
    {
        if (startLine < 1 || lineCount is < 1 or > 500)
            throw new InvalidOperationException("startLine doit être positif et lineCount compris entre 1 et 500.");
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (startLine > lines.Length)
            return $"Fin de {source} : {lines.Length} lignes.";
        var selected = lines.Skip(startLine - 1).Take(lineCount).ToList();
        var body = string.Join('\n', selected);
        if (startLine == 1 && selected.Count == lines.Length && body.Length <= maxChars)
            return text;
        // La ligne de continuation reste exacte, même si une ligne isolée dépasse la limite de taille.
        while (selected.Count > 1 && body.Length > maxChars)
        {
            selected.RemoveAt(selected.Count - 1);
            body = string.Join('\n', selected);
        }
        var longLine = body.Length > maxChars;
        if (longLine)
            body = body[..maxChars] + "\n[Ligne trop longue : extrait tronqué.]";
        var end = startLine + selected.Count - 1;
        return $"{source} · lignes {startLine}–{end}/{lines.Length}\n" + body
            + (end < lines.Length ? $"\n[Suite : startLine={end + 1}.]" : "");
    }
}
