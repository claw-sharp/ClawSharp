// TS origin: ./query.ts, ./services/api/claude.ts
// TS parity status: ports the per-iteration model request builder boundary that the TypeScript query loop effectively uses before each deps.callModel() attempt; live transport and retry semantics remain downstream.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryModelIterationRequestBuilder
{
    QueryModelHttpStreamingRequest Build(
        QueryTurnRequest request,
        QueryLoopState state,
        ClawSharpSettings settings);
}
