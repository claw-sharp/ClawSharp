namespace ClawSharp.AgentHost.Contracts;

public sealed record ListWorkspaceFilesResponse(
    string ProjectId,
    string WorkspaceRoot,
    IReadOnlyList<string> Files);
