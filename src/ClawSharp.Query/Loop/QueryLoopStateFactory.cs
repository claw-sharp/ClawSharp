// TS parity status: ports the initial TypeScript query-loop state shape and default values without inventing the missing model-backed recursive loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryLoopStateFactory
{
    public static QueryLoopState CreateInitial(
        IReadOnlyList<ChatMessage> messages,
        QueryToolUseContextState? toolUseContext = null,
        QueryTokenBudgetTracker? tokenBudgetTracker = null,
        int? maxOutputTokensOverride = null,
        Task<QueryToolUseSummaryMessage?>? pendingToolUseSummary = null,
        string? previousResponseId = null,
        int? previousResponseMessageCount = null,
        IReadOnlyList<string>? previousResponseItems = null)
    {
        return new QueryLoopState(
            messages,
            TurnCount: 1,
            ToolUseContext: toolUseContext ?? QueryToolUseContextState.Empty,
            TokenBudgetTracker: tokenBudgetTracker,
            AutoCompactTracking: null,
            MaxOutputTokensRecoveryCount: 0,
            HasAttemptedReactiveCompact: false,
            MaxOutputTokensOverride: maxOutputTokensOverride,
            PendingToolUseSummary: pendingToolUseSummary,
            StopHookActive: null,
            Transition: null,
            PreviousResponseId: previousResponseId,
            PreviousResponseMessageCount: previousResponseMessageCount,
            PreviousResponseItems: previousResponseItems);
    }
}
