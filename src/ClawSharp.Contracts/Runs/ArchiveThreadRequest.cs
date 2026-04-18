namespace ClawSharp.Contracts.Runs;

public sealed record ArchiveThreadRequest(
    string ProjectId,
    string ThreadId);
