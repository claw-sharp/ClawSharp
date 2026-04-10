using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryAutoCompactRunner
{
    Task<QueryAutoCompactResult> TryCompactAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default);
}

public sealed record QueryAutoCompactResult(
    QueryLoopState State,
    bool Compacted);

public sealed class NoOpQueryAutoCompactRunner : IQueryAutoCompactRunner
{
    public Task<QueryAutoCompactResult> TryCompactAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new QueryAutoCompactResult(state, Compacted: false));
    }
}
