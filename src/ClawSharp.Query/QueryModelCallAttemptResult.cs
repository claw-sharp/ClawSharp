// TS origin: ./query.ts, ./query/deps.ts, ./services/api/claude.ts
// TS parity status: ports the model-call attempt result contract that the TypeScript query loop uses to distinguish successful streamed attempts from fallback-triggered retries; fallback handling remains intentionally unported in C#.
namespace ClawSharp.Query;

public enum QueryModelCallAttemptOutcome
{
    Completed,
    FallbackRequested
}

public sealed record QueryModelCallAttemptResult(
    QueryModelCallAttemptOutcome Outcome,
    QueryIterationResult? IterationResult = null,
    string? OriginalModel = null,
    string? FallbackModel = null,
    int? TurnOutputTokens = null)
{
    public static QueryModelCallAttemptResult Completed(QueryIterationResult iterationResult, int? turnOutputTokens = null)
    {
        ArgumentNullException.ThrowIfNull(iterationResult);
        return new QueryModelCallAttemptResult(
            QueryModelCallAttemptOutcome.Completed,
            iterationResult,
            TurnOutputTokens: turnOutputTokens);
    }

    public static QueryModelCallAttemptResult FallbackRequested(string originalModel, string fallbackModel)
    {
        return new QueryModelCallAttemptResult(
            QueryModelCallAttemptOutcome.FallbackRequested,
            IterationResult: null,
            OriginalModel: originalModel,
            FallbackModel: fallbackModel);
    }
}
