// TS parity status: ports the TypeScript auto-compact tracking payload carried between query-loop iterations; the live compaction runtime remains unported.
namespace ClawSharp.Query;

public sealed record QueryAutoCompactTrackingState(
    bool Compacted,
    int TurnCounter,
    string TurnId,
    int? ConsecutiveFailures = null);
