// TS origin: ./query.ts, ./services/compact/compact.ts, ./services/api/claude.ts
// TS parity status: ports the reactive-compact summary-generation seam beneath the C# overflow recovery path; the live compaction model call and summary-message shaping remain delegated to later runtime ports.
// TS baseline note: the real reactive compaction runtime source is not present locally because ./services/compact/reactiveCompact.ts is still a stub, so this seam is documented against the shared compact behavior visible in ./services/compact/compact.ts and ./services/api/claude.ts.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryReactiveCompactModelCallRunner
{
    Task<QueryCompactionResult?> TryCompactAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpQueryReactiveCompactModelCallRunner : IQueryReactiveCompactModelCallRunner
{
    public Task<QueryCompactionResult?> TryCompactAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<QueryCompactionResult?>(null);
    }
}
