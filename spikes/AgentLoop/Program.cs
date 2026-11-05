using AgentYattus.Spikes;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// Usage : dotnet run --project spikes/AgentLoop -- <dossier-workspace> "<objectif>"
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage : dotnet run --project spikes/AgentLoop -- <dossier-workspace> \"<objectif>\"");
    return 1;
}

var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
var apiKey = configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException(
        "Clé d'API absente. Lance : dotnet user-secrets set \"Anthropic:ApiKey\" \"<ta-clé>\" --project spikes/AgentLoop");

// Pas de Temperature, TopP ni TopK dans ChatOptions : l'API Anthropic les refuse et l'adaptateur les ignore.
using IChatClient chatClient = new AnthropicClient { ApiKey = apiKey }
    .AsIChatClient("claude-haiku-5-5", defaultMaxOutputTokens: 16_000);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var tools = new WorkspaceTools(args[0]);
var loop = new AgentLoop(chatClient, tools.AsAIFunctions(), maxIterations: 10);
var result = await loop.RunAsync(args[1], cancellation.Token);

foreach (var content in result.History.SelectMany(message => message.Contents))
{
    var line = content switch
    {
        FunctionCallContent call => $"→ {call.Name}({string.Join(", ", call.Arguments?.Select(a => $"{a.Key}: {a.Value}") ?? [])})",
        FunctionResultContent toolResult => $"← {Truncate(toolResult.Result?.ToString(), 160)}",
        TextReasoningContent => "· réflexion du modèle (contenu non affiché)",
        _ => null,
    };

    if (line is not null)
    {
        Console.WriteLine(line);
    }
}

Console.WriteLine();
Console.WriteLine($"Statut : {result.Status} · {result.Iterations} appel(s) au modèle · {result.TotalTokens} tokens");
Console.WriteLine(result.FinalText);
return 0;

static string Truncate(string? text, int max) =>
    text is null ? "" : text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "…");
