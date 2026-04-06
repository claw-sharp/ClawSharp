// TS parity status: ports the TypeScript loop's terminal-vs-continue iteration boundary as explicit C# result contracts; full recursive query behavior still depends on the missing model-backed iteration producer.
namespace ClawSharp.Query;

public abstract record QueryIterationResult(
    QueryLoopState State);

public sealed record QueryTerminalIterationResult(
    QueryLoopTerminal Terminal,
    QueryLoopState State) : QueryIterationResult(State);

public sealed record QueryContinueIterationResult(
    QueryLoopTransition Transition,
    QueryTurnRequest NextRequest,
    QueryLoopState State) : QueryIterationResult(State);
