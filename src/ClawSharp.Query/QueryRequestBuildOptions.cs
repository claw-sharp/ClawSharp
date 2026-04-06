// TS parity status: ports the request-construction options ClawSharp can currently model directly from the TypeScript baseline; provider- and transport-specific options remain intentionally absent.
namespace ClawSharp.Query;

public sealed record QueryRequestBuildOptions(
    IReadOnlyList<string>? SystemPrompt = null,
    IReadOnlyDictionary<string, string>? SystemContext = null,
    IReadOnlyDictionary<string, string>? UserContext = null,
    bool UseGlobalCacheScope = false,
    bool SkipGlobalCacheForSystemPrompt = false,
    bool EnablePromptCaching = false,
    bool SkipCacheWrite = false,
    bool IncludeUserContextInTestEnvironment = false,
    QueryTaskBudget? TaskBudget = null,
    bool ShouldIncludeFirstPartyOnlyBetas = false);
