namespace ClawSharp.Core;

public sealed record FileHistorySnapshotEntry(
    FileHistorySnapshot Snapshot,
    bool IsSnapshotUpdate);
