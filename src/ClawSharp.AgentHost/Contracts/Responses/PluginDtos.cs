namespace ClawSharp.AgentHost.Contracts;

public sealed record PluginValidationIssueDto(
    string Path,
    string Message,
    bool IsWarning);

public sealed record PluginOptionDto(
    string Key,
    string Type,
    string Title,
    string Description,
    bool Required,
    bool Multiple,
    bool Sensitive,
    bool HasValue,
    object? Value,
    object? DefaultValue,
    double? Min,
    double? Max);

public sealed record PluginSummaryDto(
    string PluginId,
    string Name,
    string? Description,
    string? Version,
    bool Enabled,
    bool IsBundled,
    string InstallPath,
    string Scope,
    string? InstalledAt,
    string? LastUpdated,
    string? GitCommitSha,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> OutputStyles,
    IReadOnlyList<string> HookFiles,
    IReadOnlyList<string> HookEvents,
    IReadOnlyList<PluginValidationIssueDto> ValidationIssues,
    IReadOnlyList<PluginOptionDto> Options);
