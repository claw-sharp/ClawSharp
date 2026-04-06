// TS parity status: ports the immutable model-turn query context carried across TypeScript query-loop iterations; current C# preserves the prompt/context/query-source contract even though live model transport remains unported.
namespace ClawSharp.Query;

public sealed record QueryModelTurnContext(
    IReadOnlyList<string> SystemPrompt,
    IReadOnlyDictionary<string, string> UserContext,
    IReadOnlyDictionary<string, string> SystemContext,
    string QuerySource)
{
    public static QueryModelTurnContext ReplMainThread { get; } = new(
        [],
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        "repl_main_thread");
}
