// TS origin: ./QueryEngine.ts, ./query.ts
// TS parity status: simplified foundation only, not a 1:1 translation yet.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryTurnRequest(
    string SessionId,
    string UserInput,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ToolCallRequest> RequestedTools,
    QueryModelTurnContext? ModelTurnContext = null,
    int? MaxTurns = null,
    QueryAbortReason? AbortReason = null,
    QueryToolUseContextState? InitialToolUseContext = null,
    QueryTurnExecutionMode ExecutionMode = QueryTurnExecutionMode.Auto,
    QueryTaskBudget? TaskBudget = null,
    string? FallbackModel = null)
{
    public static QueryTurnRequest Create(ConversationSession session, string userInput)
    {
        return new QueryTurnRequest(
            session.Id,
            userInput.Trim(),
            DateTimeOffset.UtcNow,
            [],
            QueryModelTurnContext.ReplMainThread,
            MaxTurns: null,
            AbortReason: null,
            InitialToolUseContext: null,
            ExecutionMode: QueryTurnExecutionMode.ModelBacked,
            TaskBudget: null,
            FallbackModel: null);
    }

    public static QueryTurnRequest Create(
        ConversationSession session,
        string userInput,
        IReadOnlyList<ToolCallRequest> requestedTools)
    {
        return new QueryTurnRequest(
            session.Id,
            userInput.Trim(),
            DateTimeOffset.UtcNow,
            requestedTools,
            QueryModelTurnContext.ReplMainThread,
            MaxTurns: null,
            AbortReason: null,
            InitialToolUseContext: null,
            ExecutionMode: QueryTurnExecutionMode.ExplicitTool,
            TaskBudget: null,
            FallbackModel: null);
    }

    public static QueryTurnRequest Create(
        ConversationSession session,
        string userInput,
        IReadOnlyList<ToolCallRequest> requestedTools,
        int? maxTurns)
    {
        return new QueryTurnRequest(
            session.Id,
            userInput.Trim(),
            DateTimeOffset.UtcNow,
            requestedTools,
            QueryModelTurnContext.ReplMainThread,
            MaxTurns: maxTurns,
            AbortReason: null,
            InitialToolUseContext: null,
            ExecutionMode: requestedTools.Count > 0
                ? QueryTurnExecutionMode.ExplicitTool
                : QueryTurnExecutionMode.ModelBacked,
            TaskBudget: null,
            FallbackModel: null);
    }
}
