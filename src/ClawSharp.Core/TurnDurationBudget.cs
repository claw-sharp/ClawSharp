// TS parity status: ports the TypeScript turn-duration budget payload used by turn_duration system messages; live token-budget emission is still blocked on the missing query loop.
namespace ClawSharp.Core;

public sealed record TurnDurationBudget(
    int Tokens,
    int Limit,
    int Nudges);
