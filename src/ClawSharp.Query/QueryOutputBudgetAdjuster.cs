// TS origin: ./services/api/claude.ts
// TS parity status: ports the current TypeScript non-streaming max-token cap and thinking-budget adjustment logic for query request snapshots.
namespace ClawSharp.Query;

public static class QueryOutputBudgetAdjuster
{
    public static QueryModelRequest AdjustForNonStreaming(
        QueryModelRequest request,
        int maxTokensCap)
    {
        if (request.MaxTokens is null)
        {
            return request;
        }

        var cappedMaxTokens = Math.Min(request.MaxTokens.Value, maxTokensCap);
        var adjustedThinking = request.Thinking;

        if (adjustedThinking?.Type == "enabled" && adjustedThinking.BudgetTokens is not null)
        {
            adjustedThinking = adjustedThinking with
            {
                BudgetTokens = Math.Min(adjustedThinking.BudgetTokens.Value, cappedMaxTokens - 1)
            };
        }

        return request with
        {
            MaxTokens = cappedMaxTokens,
            Thinking = adjustedThinking
        };
    }
}
