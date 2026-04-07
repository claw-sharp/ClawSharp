// TS parity status: ports the reactive-compact prompt-build step by slicing messages from the last compact boundary and shaping the base compact prompt with hook-provided custom instructions; partial compaction, preserved-tail shaping, and cache-safe request metadata remain delegated to later runtime ports.
// TS baseline note: because ./services/compact/reactiveCompact.ts is only a stub in this checkout, this prompt-building step follows the shared compact prompt contract from ./services/compact/compact.ts and ./services/compact/prompt.ts instead of claiming a direct reactive-only source map that does not exist locally yet.
namespace ClawSharp.Query;

public sealed class QueryReactiveCompactPromptBuilder : IQueryReactiveCompactPromptBuilder
{
    public Task<QueryReactiveCompactPromptBuildResult?> BuildAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default)
    {
        var messagesToCompact = QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(context.PriorState.Messages);
        if (messagesToCompact.Count == 0)
        {
            return Task.FromResult<QueryReactiveCompactPromptBuildResult?>(null);
        }

        return Task.FromResult<QueryReactiveCompactPromptBuildResult?>(
            new QueryReactiveCompactPromptBuildResult(
                messagesToCompact,
                QueryCompactPromptFactory.CompactSystemPrompt,
                QueryCompactPromptFactory.GetCompactPrompt(hookResult.CustomInstructions)));
    }
}
