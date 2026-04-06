// TS origin: ./query.ts
// TS parity status: ports the recovery decision seam for prompt-too-long and media-size assistant API errors beneath the C# model-backed iteration runner; concrete collapse-drain and reactive-compact execution remain delegated to later runtime ports.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryPromptOverflowRecoveryRunner
{
    Task<QueryContinueIterationResult?> TryRecoverAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default);
}
