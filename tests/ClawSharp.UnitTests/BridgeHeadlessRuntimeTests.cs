// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeHeadlessRuntimeTests
{
    [Fact]
    public async Task RunAsync_Throws_Permanent_Error_When_Trust_Not_Accepted()
    {
        var dependencies = CreateDependencies(checkHasTrustDialogAccepted: () => false);

        var error = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(() =>
            BridgeHeadlessRuntime.RunAsync(
                new HeadlessBridgeOptions("D:\\repo", SpawnMode.SameDir, 2, Sandbox: true, CreateSessionOnStart: false),
                dependencies));

        Assert.Equal(
            "Workspace not trusted: D:\\repo. Run `clawsharp` in that directory first to accept the trust dialog.",
            error.Message);
    }

    [Fact]
    public async Task RunAsync_Throws_Login_Error_When_No_Access_Token()
    {
        var dependencies = CreateDependencies(getAccessToken: () => null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BridgeHeadlessRuntime.RunAsync(
                new HeadlessBridgeOptions("D:\\repo", SpawnMode.SameDir, 2, Sandbox: true, CreateSessionOnStart: false),
                dependencies));

        Assert.Equal(BridgeConstants.BridgeLoginError, error.Message);
    }

    [Fact]
    public async Task RunAsync_Throws_Permanent_Error_For_NonLocal_Http_Or_Invalid_Worktree_Mode()
    {
        var httpDependencies = CreateDependencies(getBridgeBaseUrl: () => "http://example.com");
        var worktreeDependencies = CreateDependencies(isWorktreeAvailable: _ => false);

        var httpError = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(() =>
            BridgeHeadlessRuntime.RunAsync(
                new HeadlessBridgeOptions("D:\\repo", SpawnMode.SameDir, 2, Sandbox: true, CreateSessionOnStart: false),
                httpDependencies));
        var worktreeError = await Assert.ThrowsAsync<BridgeHeadlessPermanentError>(() =>
            BridgeHeadlessRuntime.RunAsync(
                new HeadlessBridgeOptions("D:\\repo", SpawnMode.Worktree, 2, Sandbox: true, CreateSessionOnStart: false),
                worktreeDependencies));

        Assert.Equal("Remote Control base URL uses HTTP. Only HTTPS or localhost HTTP is allowed.", httpError.Message);
        Assert.Equal(
            "Worktree mode requires a git repository or WorktreeCreate hooks. Directory D:\\repo has neither.",
            worktreeError.Message);
    }

    [Fact]
    public async Task RunAsync_Builds_Config_Delegates_To_Startup_And_Runs_Loop()
    {
        var logs = new List<string>();
        BridgeConfig? capturedConfig = null;
        string? capturedEnvironmentId = null;
        string? capturedEnvironmentSecret = null;
        string? capturedInitialSessionId = null;
        var startupCoordinator = new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, title, _) => Task.FromResult<string?>($"session-for-{title}"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));
        var dependencies = CreateDependencies(
            startupCoordinator: startupCoordinator,
            log: logs.Add,
            runLoopAsync: (config, environmentId, environmentSecret, logger, initialSessionId, _) =>
            {
                capturedConfig = config;
                capturedEnvironmentId = environmentId;
                capturedEnvironmentSecret = environmentSecret;
                capturedInitialSessionId = initialSessionId;
                logger.AddSession("session_123", "https://example.com");
                logger.RemoveSession("session_123");
                return Task.CompletedTask;
            });

        await BridgeHeadlessRuntime.RunAsync(
            new HeadlessBridgeOptions(
                Dir: "D:\\repo",
                SpawnMode: SpawnMode.SameDir,
                Capacity: 4,
                Sandbox: false,
                CreateSessionOnStart: true,
                Name: "named"),
            dependencies);

        Assert.NotNull(capturedConfig);
        Assert.Equal("D:\\repo", capturedConfig!.Dir);
        Assert.Equal(4, capturedConfig.MaxSessions);
        Assert.Equal(SpawnMode.SameDir, capturedConfig.SpawnMode);
        Assert.Equal("https://api.example.com", capturedConfig.ApiBaseUrl);
        Assert.Equal("wss://ingress.example.com", capturedConfig.SessionIngressUrl);
        Assert.Equal("env_123", capturedEnvironmentId);
        Assert.Equal("secret_123", capturedEnvironmentSecret);
        Assert.Equal("session-for-named", capturedInitialSessionId);
        Assert.Contains(logs, line => line.Contains("registered environmentId=env_123 dir=D:\\repo spawnMode=same-dir capacity=4", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("created initial session session-for-named", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("session attached session_123", StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("session detached session_123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Wraps_Startup_Failures_As_Registration_Failures()
    {
        var failingStartupCoordinator = new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, _, _) => throw new BridgeFatalError("expired", 410, "environment_expired"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));
        var dependencies = CreateDependencies(startupCoordinator: failingStartupCoordinator);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BridgeHeadlessRuntime.RunAsync(
                new HeadlessBridgeOptions("D:\\repo", SpawnMode.SameDir, 2, Sandbox: true, CreateSessionOnStart: true),
                dependencies));

        Assert.Contains("Bridge registration failed: expired", error.Message, StringComparison.Ordinal);
    }

    private static BridgeHeadlessRuntimeDependencies CreateDependencies(
        Func<bool>? checkHasTrustDialogAccepted = null,
        Func<string?>? getAccessToken = null,
        Func<string>? getBridgeBaseUrl = null,
        Func<string?>? getSessionIngressUrl = null,
        Func<string, Task<string>>? getBranchAsync = null,
        Func<string, Task<string?>>? getRemoteUrlAsync = null,
        Func<string, bool>? isWorktreeAvailable = null,
        BridgeStartupCoordinator? startupCoordinator = null,
        Func<BridgeConfig, string, string, IBridgeLogger, string?, CancellationToken, Task>? runLoopAsync = null,
        Action<string>? log = null)
    {
        startupCoordinator ??= new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, title, _) => Task.FromResult<string?>($"session-for-{title}"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));

        return new BridgeHeadlessRuntimeDependencies(
            CheckHasTrustDialogAccepted: checkHasTrustDialogAccepted ?? (() => true),
            GetAccessToken: getAccessToken ?? (() => "token"),
            GetBridgeBaseUrl: getBridgeBaseUrl ?? (() => "https://api.example.com"),
            GetSessionIngressUrl: getSessionIngressUrl ?? (() => "wss://ingress.example.com"),
            GetBranchAsync: getBranchAsync ?? (_ => Task.FromResult("main")),
            GetRemoteUrlAsync: getRemoteUrlAsync ?? (_ => Task.FromResult<string?>("https://example.com/repo.git")),
            IsWorktreeAvailable: isWorktreeAvailable ?? (_ => true),
            CreateApiClient: dependencies => new StubBridgeApiClient(),
            StartupCoordinator: startupCoordinator,
            RunLoopAsync: runLoopAsync ?? ((_, _, _, _, _, _) => Task.CompletedTask),
            RunnerVersion: "1.2.3",
            Log: log ?? (_ => { }),
            OnAuth401: _ => Task.FromResult(false));
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(new BridgeRegisterEnvironmentResponse("env_123", "secret_123"));

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
            => throw new NotSupportedException();

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
