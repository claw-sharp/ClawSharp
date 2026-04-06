// TS origin: ./query.ts, ./services/compact/compact.ts
// TS parity status: ports the reactive-compact post-compact attachment seam beneath the C# overflow recovery path so plan, file, skill, and async-agent restoration can be added later without inventing attachment behavior in the loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryReactiveCompactPostCompactAttachmentBuilder
{
    Task<IReadOnlyList<ChatMessage>> BuildAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        QueryReactiveCompactHookRunResult hookResult,
        QueryCompactionResult compacted,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpQueryReactiveCompactPostCompactAttachmentBuilder : IQueryReactiveCompactPostCompactAttachmentBuilder
{
    public Task<IReadOnlyList<ChatMessage>> BuildAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        QueryReactiveCompactHookRunResult hookResult,
        QueryCompactionResult compacted,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    }
}
