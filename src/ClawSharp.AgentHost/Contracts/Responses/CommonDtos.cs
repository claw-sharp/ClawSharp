namespace ClawSharp.AgentHost.Contracts;

public sealed record ProjectSummaryDto(
    string Id,
    string Name,
    string Path,
    DateTimeOffset LastOpenedAt,
    DateTimeOffset? LastUpdatedAt,
    int ThreadCount,
    string? GitBranch);

public sealed record ThreadWorktreeMetadataDto(
    string RepoRoot,
    string? WorktreePath,
    string? WorktreeStatus,
    string? BaseBranch,
    string? BaseCommit);

public sealed record ThreadSummaryDto(
    string Id,
    string ProjectId,
    string Title,
    string Summary,
    DateTimeOffset LastUpdatedAt,
    int MessageCount,
    string TranscriptPath,
    ThreadWorktreeMetadataDto Worktree);

public sealed record ThreadMessageDto(
    string Id,
    string ThreadId,
    string Role,
    string Content,
    DateTimeOffset Timestamp);

public sealed record ThreadDetailDto(
    ThreadSummaryDto Thread,
    IReadOnlyList<ThreadMessageDto> Messages,
    bool HasMoreMessages = false,
    string? NextBeforeMessageId = null);

public sealed record ChangedFileDto(
    string Path,
    string Status,
    int Additions,
    int Deletions,
    string ThreadId);

public sealed record DiffLineDto(
    string Type,
    string Content,
    int? OldLineNumber,
    int? NewLineNumber);

public sealed record DiffHunkDto(
    string Header,
    IReadOnlyList<DiffLineDto> Lines);

public sealed record FileDiffDto(
    string FilePath,
    IReadOnlyList<DiffHunkDto> Hunks);

public sealed record DiagnosticsLogPathsDto(
    string DebugLogPath,
    string TelemetryEventsPath,
    string MetricsPath,
    string CrashPath,
    string TracePath,
    string StartupProfilePath);

public sealed record DiagnosticsRecentEventDto(
    string Id,
    string Timestamp,
    string Level,
    string Stage,
    string Message);

public sealed record DiagnosticsSummaryDto(
    string? ThreadId,
    string? SessionId,
    string Provider,
    string Model,
    string BaseUrl,
    string Transport,
    string Environment,
    string ConfigPath,
    string Uptime,
    string MemoryUsage,
    DiagnosticsLogPathsDto LogPaths,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DiagnosticsRecentEventDto> RecentEvents);

public sealed record ProviderOptionDto(
    string Id,
    string DisplayName,
    string DefaultModel,
    IReadOnlyList<string> Models,
    string BaseUrl,
    bool RequiresApiKey,
    string Description);

public sealed record ProviderCredentialStateDto(
    bool HasApiKey,
    bool HasAuthToken,
    string? AccountId,
    string Source,
    bool HasExternalCredential,
    string? ExternalCredentialPath);

public sealed record RuntimeSettingsDto(
    string Provider,
    string Model,
    string? FallbackModel,
    string PermissionMode,
    bool EnableTelemetry,
    bool FileCheckpointingEnabled,
    string BaseUrl,
    string Transport,
    string ConfigPath,
    IReadOnlyList<string> SettingsIssues,
    ProviderCredentialStateDto Credentials,
    bool HasAnyConfiguredProviderCredential);

public sealed record ProviderValidationDto(
    string Provider,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ExternalEditorLaunchDto(
    bool Launched,
    string Command,
    IReadOnlyList<string> Arguments,
    string Message);

public sealed record ApprovalRequestDto(
    string Id,
    string Action,
    string Decision,
    DateTimeOffset CreatedAt);
