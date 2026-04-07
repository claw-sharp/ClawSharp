// TS parity status: explicit no-op fallback for the prompt-too-long/media-size recovery seam until the concrete collapse-drain and reactive-compact runtimes are ported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class NoOpQueryPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
{
    public Task<QueryContinueIterationResult?> TryRecoverAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<QueryContinueIterationResult?>(null);
    }
}
