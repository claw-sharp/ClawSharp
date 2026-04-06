// TS origin: ./QueryEngine.ts, ./query.ts
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryTurnRunner
{
    IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
        QueryTurnRequest request,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default);
}
