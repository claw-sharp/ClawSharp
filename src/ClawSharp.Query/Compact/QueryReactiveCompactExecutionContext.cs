// TS parity status: ports the reactive-compact execution context and intermediate payload contracts needed by the C# prompt-overflow recovery path; the live compact-summary request shaping and compaction API semantics remain delegated to later runtime ports.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryReactiveCompactExecutionContext(
    QueryTurnRequest Request,
    QueryLoopState PriorState,
    QueryTerminalIterationResult TerminalResult,
    ConversationSession Session,
    ClawSharpSettings Settings,
    IReadOnlyList<QueryRequestTool> Tools,
    int? EstimatedPreCompactTokenCount = null,
    int? ResolvedMaxTokens = null,
    string Trigger = "manual");

public sealed record QueryReactiveCompactPromptBuildResult(
    IReadOnlyList<ChatMessage> MessagesToCompact,
    IReadOnlyList<string> SystemPrompt,
    string SummaryPrompt,
    IReadOnlyList<ChatMessage>? MessagesToKeep = null);

public sealed record QueryReactiveCompactHookRunResult(
    string? CustomInstructions = null,
    IReadOnlyList<ChatMessage>? HookResults = null,
    string? UserDisplayMessage = null)
{
    public static QueryReactiveCompactHookRunResult Empty { get; } = new();
}

public sealed record QueryReactiveCompactPostCompactHookRunResult(
    string? UserDisplayMessage = null)
{
    public static QueryReactiveCompactPostCompactHookRunResult Empty { get; } = new();
}
