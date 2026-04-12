namespace ClawSharp.AgentHost.Contracts;

public sealed record SkillSummaryDto(
    string Name,
    string Source,
    string FilePath,
    string BaseDirectory);

public sealed record ListSkillsResponse(
    string ProjectId,
    string WorkspaceRoot,
    IReadOnlyList<SkillSummaryDto> Skills);
