// TS origin: ./utils/auth.ts, ./services/api/withRetry.ts
// TS parity status: ports the minimal query-side Claude AI account-state contract needed for the TypeScript subscriber-gated retry branches; token refresh and profile fetch remain outside this contract.
namespace ClawSharp.Query;

public sealed record QueryAuthAccountState(
    bool IsClaudeAiSubscriber = false,
    bool IsEnterpriseSubscriber = false,
    string? SubscriptionType = null,
    string? RateLimitTier = null)
{
    public static QueryAuthAccountState None { get; } = new();
}
