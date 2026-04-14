using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryLoopState(
    IReadOnlyList<ChatMessage> Messages,
    int TurnCount,
    QueryToolUseContextState ToolUseContext,
    QueryTokenBudgetTracker? TokenBudgetTracker = null,
    QueryAutoCompactTrackingState? AutoCompactTracking = null,
    int MaxOutputTokensRecoveryCount = 0,
    bool HasAttemptedReactiveCompact = false,
    int? MaxOutputTokensOverride = null,
    Task<QueryToolUseSummaryMessage?>? PendingToolUseSummary = null,
    bool? StopHookActive = null,
    QueryLoopTransition? Transition = null,
    string? PreviousResponseId = null,
    int? PreviousResponseMessageCount = null,
    IReadOnlyList<string>? PreviousResponseItems = null);
