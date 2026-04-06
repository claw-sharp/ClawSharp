// TS origin: ./query.ts, ./services/compact/compact.ts, ./utils/hooks.ts
// TS parity status: ports the reactive-compact pre-compact and post-compact hook seams beneath the C# overflow recovery path so the shared compact.ts hook phases can run without inventing them inside the loop; the raw summary handoff needed to invoke the post-compact phase from the live compaction path remains delegated to later runtime ports.
namespace ClawSharp.Query;

public interface IQueryReactiveCompactHookRunner
{
    Task<QueryReactiveCompactHookRunResult> RunPreCompactAsync(
        QueryReactiveCompactExecutionContext context,
        CancellationToken cancellationToken = default);

    Task<QueryReactiveCompactPostCompactHookRunResult> RunPostCompactAsync(
        QueryReactiveCompactExecutionContext context,
        string compactSummary,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpQueryReactiveCompactHookRunner : IQueryReactiveCompactHookRunner
{
    public Task<QueryReactiveCompactHookRunResult> RunPreCompactAsync(
        QueryReactiveCompactExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(QueryReactiveCompactHookRunResult.Empty);
    }

    public Task<QueryReactiveCompactPostCompactHookRunResult> RunPostCompactAsync(
        QueryReactiveCompactExecutionContext context,
        string compactSummary,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(QueryReactiveCompactPostCompactHookRunResult.Empty);
    }
}
