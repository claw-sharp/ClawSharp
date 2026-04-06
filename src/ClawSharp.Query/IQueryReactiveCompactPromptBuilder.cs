// TS origin: ./query.ts, ./services/compact/compact.ts, ./services/compact/prompt.ts
// TS parity status: ports the reactive-compact prompt-build seam beneath the C# overflow recovery path so compaction prompt shaping can be added later without inventing summary-request behavior in the loop runner.
namespace ClawSharp.Query;

public interface IQueryReactiveCompactPromptBuilder
{
    Task<QueryReactiveCompactPromptBuildResult?> BuildAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpQueryReactiveCompactPromptBuilder : IQueryReactiveCompactPromptBuilder
{
    public Task<QueryReactiveCompactPromptBuildResult?> BuildAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<QueryReactiveCompactPromptBuildResult?>(null);
    }
}
