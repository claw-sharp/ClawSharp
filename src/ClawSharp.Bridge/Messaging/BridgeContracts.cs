namespace ClawSharp.Bridge;

public static class BridgeConstants
{
    public const int DefaultSessionTimeoutMs = 24 * 60 * 60 * 1000;

    public const string BridgeLoginInstruction =
        "Remote Control is only available with claude.ai subscriptions. Please use `/login` to sign in with your claude.ai account.";

    public const string BridgeLoginError =
        "Error: You must be logged in to use Remote Control.\n\n" +
        BridgeLoginInstruction;

    public const string RemoteControlDisconnectedMessage = "Remote Control disconnected.";
}

public enum BridgeWorkDataType
{
    Session,
    Healthcheck
}

public sealed record BridgeWorkData(
    BridgeWorkDataType Type,
    string Id);

public sealed record BridgeWorkResponse(
    string Id,
    string Type,
    string EnvironmentId,
    string State,
    BridgeWorkData Data,
    string Secret,
    string CreatedAt);

public sealed record BridgeWorkSecretSourceGitInfo(
    string Type,
    string Repo,
    string? Ref = null,
    string? Token = null);

public sealed record BridgeWorkSecretSource(
    string Type,
    BridgeWorkSecretSourceGitInfo? GitInfo = null);

public sealed record BridgeWorkSecretAuth(
    string Type,
    string Token);

public sealed record BridgeWorkSecret(
    int Version,
    string SessionIngressToken,
    string ApiBaseUrl,
    IReadOnlyList<BridgeWorkSecretSource> Sources,
    IReadOnlyList<BridgeWorkSecretAuth> Auth,
    IReadOnlyDictionary<string, string>? ClaudeCodeArgs = null,
    object? McpConfig = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool? UseCodeSessions = null);

public enum SessionDoneStatus
{
    Completed,
    Failed,
    Interrupted
}

public enum SessionActivityType
{
    ToolStart,
    Text,
    Result,
    Error
}

public sealed record SessionActivity(
    SessionActivityType Type,
    string Summary,
    long Timestamp);

public enum SpawnMode
{
    SingleSession,
    Worktree,
    SameDir
}

public enum BridgeWorkerType
{
    ClaudeCode,
    ClaudeCodeAssistant
}

public sealed record BridgeConfig(
    string Dir,
    string MachineName,
    string Branch,
    string? GitRepoUrl,
    int MaxSessions,
    SpawnMode SpawnMode,
    bool Verbose,
    bool Sandbox,
    string BridgeId,
    string WorkerType,
    string EnvironmentId,
    string ApiBaseUrl,
    string SessionIngressUrl,
    string? ReuseEnvironmentId = null,
    string? DebugFile = null,
    int? SessionTimeoutMs = null);

public sealed record PermissionResponsePayload(
    string Subtype,
    string RequestId,
    IReadOnlyDictionary<string, object?> Response);

public sealed record PermissionResponseEvent(
    string Type,
    PermissionResponsePayload Response);

public sealed record BridgeRegisterEnvironmentResponse(
    string EnvironmentId,
    string EnvironmentSecret);

public sealed record BridgeHeartbeatResponse(
    bool LeaseExtended,
    string State);

public interface IBridgeApiClient
{
    Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default);

    Task<BridgeWorkResponse?> PollForWorkAsync(
        string environmentId,
        string environmentSecret,
        CancellationToken cancellationToken = default,
        int? reclaimOlderThanMs = null);

    Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default);

    Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default);

    Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default);

    Task SendPermissionResponseEventAsync(
        string sessionId,
        PermissionResponseEvent @event,
        string sessionToken,
        CancellationToken cancellationToken = default);

    Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default);

    Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(
        string environmentId,
        string workId,
        string sessionToken,
        CancellationToken cancellationToken = default);
}

public interface ISessionHandle
{
    string SessionId { get; }

    Task<SessionDoneStatus> Done { get; }

    IReadOnlyList<SessionActivity> Activities { get; }

    SessionActivity? CurrentActivity { get; }

    string AccessToken { get; }

    IReadOnlyList<string> LastStderr { get; }

    void Kill();

    void ForceKill();

    void WriteStdin(string data);

    void UpdateAccessToken(string token);
}

public sealed record SessionSpawnOptions(
    string SessionId,
    string SdkUrl,
    string AccessToken,
    bool? UseCcrV2 = null,
    long? WorkerEpoch = null,
    Action<string>? OnFirstUserMessage = null);

public interface ISessionSpawner
{
    ISessionHandle Spawn(SessionSpawnOptions options, string dir);
}

public interface IBridgeLogger
{
    void PrintBanner(BridgeConfig config, string environmentId);

    void LogSessionStart(string sessionId, string prompt);

    void LogSessionComplete(string sessionId, long durationMs);

    void LogSessionFailed(string sessionId, string error);

    void LogStatus(string message);

    void LogVerbose(string message);

    void LogError(string message);

    void LogReconnected(long disconnectedMs);

    void UpdateIdleStatus();

    void UpdateReconnectingStatus(string delay, string elapsed);

    void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail);

    void ClearStatus();

    void SetRepoInfo(string repoName, string branch);

    void SetDebugLogPath(string path);

    void SetAttached(string sessionId);

    void UpdateFailedStatus(string error);

    void ToggleQr();

    void UpdateSessionCount(int active, int max, SpawnMode mode);

    void SetSpawnModeDisplay(SpawnMode? mode);

    void AddSession(string sessionId, string url);

    void UpdateSessionActivity(string sessionId, SessionActivity activity);

    void SetSessionTitle(string sessionId, string title);

    void RemoveSession(string sessionId);

    void RefreshDisplay();
}
