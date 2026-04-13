namespace ClawSharp.AgentHost.Contracts;

public sealed record CreateSkillResponse(
    string ProjectId,
    string WorkspaceRoot,
    SkillSummaryDto Skill,
    IReadOnlyList<SkillSummaryDto> Skills);
