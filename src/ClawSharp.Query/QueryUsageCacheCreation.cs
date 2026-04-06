// TS origin: ./services/api/logging.ts, ./services/api/claude.ts
// TS parity status: ports the cache_creation usage counters carried through the current TypeScript query runtime usage model.
namespace ClawSharp.Query;

public sealed record QueryUsageCacheCreation(
    int Ephemeral1hInputTokens,
    int Ephemeral5mInputTokens);
