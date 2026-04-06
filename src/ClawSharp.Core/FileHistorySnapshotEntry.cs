// TS origin: ./utils/sessionStorage.ts, ./types/logs.ts
namespace ClawSharp.Core;

public sealed record FileHistorySnapshotEntry(
    FileHistorySnapshot Snapshot,
    bool IsSnapshotUpdate);
