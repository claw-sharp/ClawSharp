// TS origin: ./utils/thinking.ts, ./services/api/claude.ts
// TS parity status: ports the request-side thinking config surface needed for max-token budget adjustment in the current TypeScript non-streaming fallback path.
namespace ClawSharp.Query;

public sealed record QueryThinkingConfig(
    string Type,
    int? BudgetTokens = null);
