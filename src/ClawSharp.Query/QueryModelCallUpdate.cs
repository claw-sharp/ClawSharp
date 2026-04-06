// TS origin: ./query.ts, ./services/api/claude.ts
// TS parity status: ports the streamed model-call update envelope so C# can carry raw runtime events and the final per-attempt outcome under the model-backed iteration boundary.
namespace ClawSharp.Query;

public sealed record QueryModelCallUpdate(
    QueryRuntimeEvent? RuntimeEvent = null,
    QueryModelCallAttemptResult? AttemptResult = null);
