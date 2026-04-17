namespace ClawSharp.AgentHost.Contracts;

public sealed record AgentSummaryDto(
    string Identifier,
    string WhenToUse,
    string Source,
    string BaseDirectory,
    string? FilePath,
    string SystemPrompt,
    IReadOnlyList<string>? Tools,
    IReadOnlyList<string>? DisallowedTools,
    IReadOnlyList<string>? Skills,
    string? Color,
    string? Model,
    string? PermissionMode,
    int? MaxTurns,
    string? Filename,
    bool? Background,
    string? InitialPrompt,
    string? Memory,
    string? Isolation,
    bool OmitClaudeMd);

public sealed record AgentProposalDto(
    string Identifier,
    string WhenToUse,
    string SystemPrompt,
    string? Model = null,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? DisallowedTools = null,
    IReadOnlyList<string>? Skills = null,
    string? Color = null,
    string? PermissionMode = null,
    int? MaxTurns = null,
    bool? Background = null,
    string? InitialPrompt = null,
    string? Memory = null,
    string? Isolation = null,
    bool OmitClaudeMd = false);

public sealed record ListAgentsResponse(
    string ProjectId,
    string WorkspaceRoot,
    IReadOnlyList<AgentSummaryDto> Agents);
