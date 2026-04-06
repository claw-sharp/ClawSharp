// TS origin: ./services/api/claude.ts
// TS parity status: ports the request output_config foundation needed for task-budget shaping; broader thinking and structured-output fields remain blocked on the real model call path.
namespace ClawSharp.Query;

public sealed record QueryRequestOutputConfig(
    QueryTaskBudget? TaskBudget = null);
