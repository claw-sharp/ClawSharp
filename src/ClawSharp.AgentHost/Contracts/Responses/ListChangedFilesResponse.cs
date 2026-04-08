namespace ClawSharp.AgentHost.Contracts;

public sealed record ListChangedFilesResponse(
    string ProjectId,
    string? ThreadId,
    IReadOnlyList<ChangedFileDto> Files);
