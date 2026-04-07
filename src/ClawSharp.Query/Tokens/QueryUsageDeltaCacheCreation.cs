// TS parity status: ports the partial cache_creation usage delta shape consumed by the current TypeScript streaming usage updater.
namespace ClawSharp.Query;

public sealed record QueryUsageDeltaCacheCreation(
    int? Ephemeral1hInputTokens = null,
    int? Ephemeral5mInputTokens = null);
