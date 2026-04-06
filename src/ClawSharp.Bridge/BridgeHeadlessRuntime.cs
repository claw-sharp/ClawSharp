using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed class BridgeHeadlessPermanentError : Exception
{
    public BridgeHeadlessPermanentError(string message)
        : base(message)
    {
    }
}

public sealed record HeadlessBridgeOptions(
    string Dir,
    SpawnMode SpawnMode,
    int Capacity,
    bool Sandbox,
    bool CreateSessionOnStart,
    string? Name = null,
    string? PermissionMode = null,
    int? SessionTimeoutMs = null);

public sealed record BridgeHeadlessRuntimeDependencies(
    Func<bool> CheckHasTrustDialogAccepted,
    Func<string?> GetAccessToken,
    Func<string> GetBridgeBaseUrl,
    Func<string?> GetSessionIngressUrl,
    Func<string, Task<string>> GetBranchAsync,
    Func<string, Task<string?>> GetRemoteUrlAsync,
    Func<string, bool> IsWorktreeAvailable,
    Func<BridgeApiClientDependencies, IBridgeApiClient> CreateApiClient,
    BridgeStartupCoordinator StartupCoordinator,
    Func<BridgeConfig, string, string, IBridgeLogger, string?, CancellationToken, Task> RunLoopAsync,
    string RunnerVersion,
    Action<string> Log,
    Func<string, Task<bool>>? OnAuth401 = null,
    Func<string?>? GetTrustedDeviceToken = null,
    Func<string>? CreateBridgeId = null,
    Func<string>? CreateEnvironmentId = null,
    Func<string>? GetMachineName = null);

public static class BridgeHeadlessRuntime
{
    public static async Task RunAsync(
        HeadlessBridgeOptions options,
        BridgeHeadlessRuntimeDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);

        var dir = options.Dir;
        if (!dependencies.CheckHasTrustDialogAccepted())
        {
            throw new BridgeHeadlessPermanentError(
                $"Workspace not trusted: {dir}. Run `{AppMetadata.CommandName}` in that directory first to accept the trust dialog.");
        }

        if (string.IsNullOrEmpty(dependencies.GetAccessToken()))
        {
            throw new InvalidOperationException(BridgeConstants.BridgeLoginError);
        }

        var baseUrl = dependencies.GetBridgeBaseUrl();
        if (baseUrl.StartsWith("http://", StringComparison.Ordinal) &&
            !baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeHeadlessPermanentError(
                "Remote Control base URL uses HTTP. Only HTTPS or localhost HTTP is allowed.");
        }

        if (options.SpawnMode == SpawnMode.Worktree && !dependencies.IsWorktreeAvailable(dir))
        {
            throw new BridgeHeadlessPermanentError(
                $"Worktree mode requires a git repository or WorktreeCreate hooks. Directory {dir} has neither.");
        }

        var branch = await dependencies.GetBranchAsync(dir);
        var gitRepoUrl = await dependencies.GetRemoteUrlAsync(dir);
        var machineName = dependencies.GetMachineName?.Invoke() ?? Environment.MachineName;
        var bridgeId = dependencies.CreateBridgeId?.Invoke() ?? Guid.NewGuid().ToString("D");
        var environmentId = dependencies.CreateEnvironmentId?.Invoke() ?? Guid.NewGuid().ToString("D");
        var sessionIngressUrl = dependencies.GetSessionIngressUrl() ?? baseUrl;

        var config = new BridgeConfig(
            Dir: dir,
            MachineName: machineName,
            Branch: branch,
            GitRepoUrl: gitRepoUrl,
            MaxSessions: options.Capacity,
            SpawnMode: options.SpawnMode,
            Verbose: false,
            Sandbox: options.Sandbox,
            BridgeId: bridgeId,
            WorkerType: "claude_code",
            EnvironmentId: environmentId,
            ApiBaseUrl: baseUrl,
            SessionIngressUrl: sessionIngressUrl,
            SessionTimeoutMs: options.SessionTimeoutMs);

        var api = dependencies.CreateApiClient(
            new BridgeApiClientDependencies(
                BaseUrl: baseUrl,
                GetAccessToken: dependencies.GetAccessToken,
                RunnerVersion: dependencies.RunnerVersion,
                OnDebug: dependencies.Log,
                OnAuth401: dependencies.OnAuth401,
                GetTrustedDeviceToken: dependencies.GetTrustedDeviceToken));

        BridgeStartupResult startupResult;
        try
        {
            startupResult = await dependencies.StartupCoordinator.StartAsync(
                new BridgeStartupRequest(
                    config,
                    options.CreateSessionOnStart,
                    Title: options.Name,
                    PermissionMode: options.PermissionMode),
                cancellationToken);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Bridge registration failed: {error.Message}", error);
        }

        var logger = CreateHeadlessBridgeLogger(dependencies.Log);
        logger.PrintBanner(config, startupResult.EnvironmentId);
        if (!string.IsNullOrEmpty(startupResult.InitialSessionId) && options.CreateSessionOnStart)
        {
            dependencies.Log($"created initial session {startupResult.InitialSessionId}");
        }

        await dependencies.RunLoopAsync(
            config,
            startupResult.EnvironmentId,
            startupResult.EnvironmentSecret,
            logger,
            startupResult.InitialSessionId,
            cancellationToken);
    }

    public static IBridgeLogger CreateHeadlessBridgeLogger(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return new HeadlessBridgeLogger(log);
    }

    private sealed class HeadlessBridgeLogger(Action<string> log) : IBridgeLogger
    {
        public void PrintBanner(BridgeConfig config, string environmentId)
        {
            log($"registered environmentId={environmentId} dir={config.Dir} spawnMode={ToTsSpawnMode(config.SpawnMode)} capacity={config.MaxSessions}");
        }

        public void LogSessionStart(string sessionId, string prompt) => log($"session start {sessionId}");

        public void LogSessionComplete(string sessionId, long durationMs) => log($"session complete {sessionId} ({durationMs}ms)");

        public void LogSessionFailed(string sessionId, string error) => log($"session failed {sessionId}: {error}");

        public void LogStatus(string message) => log(message);

        public void LogVerbose(string message) => log(message);

        public void LogError(string message) => log($"error: {message}");

        public void LogReconnected(long disconnectedMs) => log($"reconnected after {disconnectedMs}ms");

        public void UpdateIdleStatus()
        {
        }

        public void UpdateReconnectingStatus(string delay, string elapsed)
        {
        }

        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail)
        {
        }

        public void ClearStatus()
        {
        }

        public void SetRepoInfo(string repoName, string branch)
        {
        }

        public void SetDebugLogPath(string path)
        {
        }

        public void SetAttached(string sessionId)
        {
        }

        public void UpdateFailedStatus(string error)
        {
        }

        public void ToggleQr()
        {
        }

        public void UpdateSessionCount(int active, int max, SpawnMode mode)
        {
        }

        public void SetSpawnModeDisplay(SpawnMode? mode)
        {
        }

        public void AddSession(string sessionId, string url) => log($"session attached {sessionId}");

        public void UpdateSessionActivity(string sessionId, SessionActivity activity)
        {
        }

        public void SetSessionTitle(string sessionId, string title)
        {
        }

        public void RemoveSession(string sessionId) => log($"session detached {sessionId}");

        public void RefreshDisplay()
        {
        }

        private static string ToTsSpawnMode(SpawnMode mode)
        {
            return mode switch
            {
                SpawnMode.SingleSession => "single-session",
                SpawnMode.Worktree => "worktree",
                SpawnMode.SameDir => "same-dir",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
    }
}
