// TS parity status: ports the pure max_output_tokens recovery branch decisions from the current TypeScript query loop; wiring those decisions into the live model-backed loop remains blocked.
namespace ClawSharp.Query;

public sealed record QueryMaxOutputTokensRecoveryDecision(
    QueryLoopTransition Transition,
    int? NextMaxOutputTokensOverride = null,
    int NextRecoveryCount = 0,
    string? RecoveryMessageContent = null);
