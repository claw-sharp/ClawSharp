// TS origin: ./query.ts
// TS parity status: ports the current TypeScript abort reason distinction needed by the aborted-tools branch; broader abort-controller state and queued interrupt semantics remain blocked on the missing model-backed runtime.
namespace ClawSharp.Query;

public enum QueryAbortReason
{
    Cancellation,
    Interrupt
}
