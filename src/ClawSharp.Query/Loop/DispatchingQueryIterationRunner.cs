// TS parity status: ports the C# routing boundary between explicit-tool and model-backed iterations so continuation branches can move between them under the shared outer query loop; the model-backed branch itself remains unported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class DispatchingQueryIterationRunner : IQueryIterationRunner
{
    private readonly IQueryIterationRunner _explicitToolIterationRunner;
    private readonly IQueryIterationRunner _modelBackedIterationRunner;

    public DispatchingQueryIterationRunner(
        IQueryIterationRunner explicitToolIterationRunner,
        IQueryIterationRunner modelBackedIterationRunner)
    {
        _explicitToolIterationRunner = explicitToolIterationRunner;
        _modelBackedIterationRunner = modelBackedIterationRunner;
    }

    public Task<QueryIterationResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        return ResolveRunner(request).RunAsync(
            request,
            state,
            session,
            settings,
            emitEvent,
            cancellationToken);
    }

    private IQueryIterationRunner ResolveRunner(QueryTurnRequest request)
    {
        return request.ExecutionMode switch
        {
            QueryTurnExecutionMode.ExplicitTool => _explicitToolIterationRunner,
            QueryTurnExecutionMode.ModelBacked => _modelBackedIterationRunner,
            QueryTurnExecutionMode.Auto when request.RequestedTools.Count > 0 => _explicitToolIterationRunner,
            _ => _modelBackedIterationRunner
        };
    }
}
