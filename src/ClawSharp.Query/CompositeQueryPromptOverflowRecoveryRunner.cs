// TS parity status: ports the TypeScript recovery ordering at the current C# seam level by trying prompt-overflow recovery strategies in sequence until one returns a continuation.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class CompositeQueryPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
{
    private readonly IReadOnlyList<IQueryPromptOverflowRecoveryRunner> _runners;

    public CompositeQueryPromptOverflowRecoveryRunner(params IReadOnlyList<IQueryPromptOverflowRecoveryRunner> runners)
    {
        _runners = runners;
    }

    public async Task<QueryContinueIterationResult?> TryRecoverAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        foreach (var runner in _runners)
        {
            var recovered = await runner.TryRecoverAsync(
                request,
                priorState,
                terminalResult,
                session,
                settings,
                emitEvent,
                cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        return null;
    }
}
