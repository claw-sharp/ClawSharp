// TS parity status: compatibility wrapper that routes the explicit-tool single-iteration runner through the new outer query-loop coordinator.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class ExplicitToolTurnRunner : IQueryTurnRunner
{
    private readonly QueryLoopRunner _queryLoopRunner;

    public ExplicitToolTurnRunner(
        ToolOrchestrator toolOrchestrator,
        IQueryStopHookRunner? stopHookRunner = null,
        IQueryIterationRunner? modelBackedIterationRunner = null,
        IPostSamplingHookRegistry? postSamplingHookRegistry = null,
        IQueryPromptOverflowRecoveryRunner? promptOverflowRecoveryRunner = null)
    {
        _queryLoopRunner = new QueryLoopRunner(
            new DispatchingQueryIterationRunner(
                new ExplicitToolIterationRunner(toolOrchestrator, stopHookRunner),
                modelBackedIterationRunner ?? new ModelBackedIterationRunner(
                    postSamplingHookRegistry,
                    promptOverflowRecoveryRunner: promptOverflowRecoveryRunner,
                    toolOrchestrator: toolOrchestrator,
                    stopHookRunner: stopHookRunner)));
    }

    public IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
        QueryTurnRequest request,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _queryLoopRunner.RunAsync(request, session, settings, cancellationToken);
    }
}
