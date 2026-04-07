// TS parity status: ports the API output_config.task_budget shape from the current TypeScript request-construction path.
namespace ClawSharp.Query;

public sealed record QueryTaskBudget(
    int Total,
    int? Remaining = null);
