namespace ClawSharp.AgentHost.Contracts;

public sealed record GetDiffResponse(
    string ProjectId,
    string? ThreadId,
    FileDiffDto Diff);
