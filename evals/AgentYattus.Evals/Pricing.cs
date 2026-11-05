namespace AgentYattus.Evals;

/// <summary>
/// Prix publics d'Anthropic en dollars par million de tokens, relevés le 2026-11-05 sur
/// https://platform.claude.com/docs/en/about-claude/pricing (tarif de base, sans cache ni lot).
/// </summary>
public static class Pricing
{
    // ponytail: table recopiée à la main ; à revérifier avant chaque évaluation, les prix changent.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> PerMillion = new()
    {
        ["claude-haiku-5-5"] = (0.10m, 0.50m),
        ["claude-sonnet-5-5"] = (2m, 10m),
        ["claude-opus-5-5"] = (4m, 20m),
    };

    // Haiku 5.5 coûte plus cher au-delà de 100 000 tokens d'entrée dans une même requête.
    private const long HaikuLongPrompt = 100_000;
    private static readonly (decimal Input, decimal Output) HaikuLong = (0.50m, 2.50m);

    public static bool Knows(string model) => PerMillion.ContainsKey(model);

    /// <summary>Le coût d'un appel au modèle.</summary>
    public static decimal Cost(string model, long inputTokens, long outputTokens)
    {
        var (input, output) = model == "claude-haiku-5-5" && inputTokens > HaikuLongPrompt
            ? HaikuLong
            : PerMillion.TryGetValue(model, out var price)
                ? price
                : throw new ArgumentException($"Prix inconnu pour le modèle {model}.", nameof(model));
        return (inputTokens * input + outputTokens * output) / 1_000_000m;
    }
}
