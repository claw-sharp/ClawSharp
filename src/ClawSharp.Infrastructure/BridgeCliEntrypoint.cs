// TS origin: ./entrypoints/cli.tsx, ./bridge/bridgeMain.ts
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using ClawSharp.Bridge;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed record BridgeCliEntrypointDependencies(
    Func<string>? GetCurrentDirectory = null,
    Func<CancellationToken, Task<bool>>? CheckHasTrustDialogAcceptedAsync = null,
    Func<string?>? GetAccessToken = null,
    Func<string>? GetBridgeBaseUrl = null,
    Func<string?>? GetSessionIngressUrl = null,
    Func<string, CancellationToken, Task<string>>? GetBranchAsync = null,
    Func<string, CancellationToken, Task<string?>>? GetRemoteUrlAsync = null,
    Func<string, bool>? IsWorktreeAvailable = null,
    Func<string, CancellationToken, Task<BridgePointerAcrossWorktreesResult?>>? ReadBridgePointerAcrossWorktreesAsync = null,
    Func<string, CancellationToken, Task>? ClearBridgePointerAsync = null,
    Func<BridgeRuntimeRequest, Action<string>, CancellationToken, Task<int>>? RunRuntimeAsync = null);

public static class BridgeCliEntrypoint
{
    private const int SpawnSessionsDefault = 32;
    private static readonly string[] BridgeCommandAliases = ["remote-control", "rc", "remote", "sync", "bridge"];
    private static readonly string[] ValidPermissionModes =
    [
        "default",
        "acceptEdits",
        "bypassPermissions",
        "dontAsk",
        "plan",
        "auto",
        "bubble"
    ];

    public static bool IsBridgeCommand(string? command)
    {
        return !string.IsNullOrWhiteSpace(command) &&
               BridgeCommandAliases.Contains(command, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        BridgeCliEntrypointDependencies? dependencies = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var resolved = dependencies ?? CreateDefaultDependencies();
        var parsed = BridgeMainArgumentUtilities.ParseArgs(
            args,
            new BridgeMainParserOptions(KairosEnabled: true));

        if (parsed.Help)
        {
            await PrintHelpAsync(stdout, cancellationToken);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
            await stderr.WriteLineAsync($"Error: {parsed.Error}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(parsed.PermissionMode) &&
            !ValidPermissionModes.Contains(parsed.PermissionMode, StringComparer.Ordinal))
        {
            await stderr.WriteLineAsync(
                $"Error: Invalid permission mode '{parsed.PermissionMode}'. Valid modes: {string.Join(", ", ValidPermissionModes)}");
            return 1;
        }

        var dir = Path.GetFullPath((resolved.GetCurrentDirectory ?? Directory.GetCurrentDirectory)());
        var checkTrustAsync = resolved.CheckHasTrustDialogAcceptedAsync
                              ?? throw new InvalidOperationException("CheckHasTrustDialogAcceptedAsync is required.");
        if (!await checkTrustAsync(cancellationToken))
        {
            await stderr.WriteLineAsync(
                $"Error: Workspace not trusted. Please run `{AppMetadata.CommandName}` in {dir} first to review and accept the workspace trust dialog.");
            return 1;
        }

        var getAccessToken = resolved.GetAccessToken
                             ?? throw new InvalidOperationException("GetAccessToken is required.");
        if (string.IsNullOrWhiteSpace(getAccessToken()))
        {
            await stderr.WriteLineAsync(BridgeConstants.BridgeLoginError);
            return 1;
        }

        var getBridgeBaseUrl = resolved.GetBridgeBaseUrl
                               ?? throw new InvalidOperationException("GetBridgeBaseUrl is required.");
        var baseUrl = getBridgeBaseUrl();
        if (baseUrl.StartsWith("http://", StringComparison.Ordinal) &&
            !baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            await stderr.WriteLineAsync(
                "Error: Remote Control base URL uses HTTP. Only HTTPS or localhost HTTP is allowed.");
            return 1;
        }

        var readBridgePointerAcrossWorktreesAsync = resolved.ReadBridgePointerAcrossWorktreesAsync
                                                    ?? throw new InvalidOperationException("ReadBridgePointerAcrossWorktreesAsync is required.");
        string? resumeSessionId = parsed.SessionId;
        string? resumePointerDir = null;
        if (parsed.ContinueSession)
        {
            var found = await readBridgePointerAcrossWorktreesAsync(dir, cancellationToken);
            if (found is null)
            {
                await stderr.WriteLineAsync(
                    $"Error: No recent session found in this directory or its worktrees. Run `{AppMetadata.RemoteControlCommand}` to start a new one.");
                return 1;
            }

            var ageMinutes = Math.Max(0, (int)Math.Round(found.Pointer.AgeMs / 60_000d));
            var ageText = ageMinutes < 60
                ? $"{ageMinutes}m"
                : $"{Math.Round(ageMinutes / 60d):0}h";
            var fromWorktree = string.Equals(found.Dir, dir, StringComparison.Ordinal)
                ? string.Empty
                : $" from worktree {found.Dir}";
            await stderr.WriteLineAsync(
                $"Resuming session {found.Pointer.SessionId} ({ageText} ago){fromWorktree}...");
            resumeSessionId = found.Pointer.SessionId;
            resumePointerDir = found.Dir;
        }

        var spawnMode = resumeSessionId is not null
            ? SpawnMode.SingleSession
            : parsed.SpawnMode ?? SpawnMode.SingleSession;
        var isWorktreeAvailable = resolved.IsWorktreeAvailable
                                  ?? throw new InvalidOperationException("IsWorktreeAvailable is required.");
        if (spawnMode == SpawnMode.Worktree)
        {
            if (!isWorktreeAvailable(dir))
            {
                await stderr.WriteLineAsync(
                    "Error: Worktree mode requires a git repository or WorktreeCreate hooks configured. Use --spawn=session for single-session mode.");
                return 1;
            }

            await stderr.WriteLineAsync(
                "Error: Worktree mode is not implemented in ClawSharp yet. Use --spawn=session or --spawn=same-dir.");
            return 1;
        }

        var capacity = spawnMode == SpawnMode.SingleSession
            ? 1
            : parsed.Capacity ?? SpawnSessionsDefault;
        var preCreateSession = parsed.CreateSessionInDir ?? true;

        var clearBridgePointerAsync = resolved.ClearBridgePointerAsync
                                      ?? throw new InvalidOperationException("ClearBridgePointerAsync is required.");
        if (resumeSessionId is null)
        {
            await clearBridgePointerAsync(dir, cancellationToken);
        }

        var getBranchAsync = resolved.GetBranchAsync
                             ?? throw new InvalidOperationException("GetBranchAsync is required.");
        var getRemoteUrlAsync = resolved.GetRemoteUrlAsync
                                ?? throw new InvalidOperationException("GetRemoteUrlAsync is required.");
        var branch = await getBranchAsync(dir, cancellationToken);
        var gitRepoUrl = await getRemoteUrlAsync(dir, cancellationToken);
        var sessionIngressUrl = resolved.GetSessionIngressUrl?.Invoke() ?? baseUrl;
        var config = new BridgeConfig(
            Dir: dir,
            MachineName: Environment.MachineName,
            Branch: branch,
            GitRepoUrl: gitRepoUrl,
            MaxSessions: capacity,
            SpawnMode: spawnMode,
            Verbose: parsed.Verbose,
            Sandbox: parsed.Sandbox,
            BridgeId: Guid.NewGuid().ToString("D"),
            WorkerType: "claude_code",
            EnvironmentId: Guid.NewGuid().ToString("D"),
            ApiBaseUrl: baseUrl,
            SessionIngressUrl: sessionIngressUrl,
            DebugFile: parsed.DebugFile,
            SessionTimeoutMs: parsed.SessionTimeoutMs is null ? null : (int)parsed.SessionTimeoutMs.Value);

        var runtimeRequest = new BridgeRuntimeRequest(
            new BridgeStartupRequest(
                Config: config,
                PreCreateSession: preCreateSession,
                Title: parsed.Name,
                PermissionMode: parsed.PermissionMode,
                ResumeSessionId: resumeSessionId,
                ResumePointerDir: resumePointerDir),
            AllowResumeOnShutdown: spawnMode == SpawnMode.SingleSession);

        var runRuntimeAsync = resolved.RunRuntimeAsync
                              ?? throw new InvalidOperationException("RunRuntimeAsync is required.");

        try
        {
            return await runRuntimeAsync(
                runtimeRequest,
                message => stdout.WriteLine(message),
                cancellationToken);
        }
        catch (Exception error)
        {
            await stderr.WriteLineAsync($"Error: {error.Message}");
            return 1;
        }
    }

    private static async Task PrintHelpAsync(TextWriter stdout, CancellationToken cancellationToken)
    {
        var help = $$"""
Remote Control - Connect your local environment to claude.ai/code

USAGE
  {{AppMetadata.RemoteControlCommand}} [options]

ALIASES
  rc, remote, sync, bridge

OPTIONS
  --name <name>                    Name for the session (shown in claude.ai/code)
  -c, --continue                   Resume the last session in this directory
  --session-id <id>                Resume a specific session by ID
  --permission-mode <mode>         Permission mode for spawned sessions
                                   ({{string.Join(", ", ValidPermissionModes)}})
  --spawn <mode>                   Spawn mode: session, same-dir, worktree
                                   (default: session)
  --capacity <N>                   Max concurrent sessions in same-dir mode
                                   (default: 32)
  --[no-]create-session-in-dir     Pre-create a session in the current directory
                                   (default: on)
  --debug-file <path>              Write debug logs to file
  -v, --verbose                    Enable verbose output
  -h, --help                       Show this help

DESCRIPTION
  Remote Control allows you to control sessions on your local device from
  claude.ai/code. Run this command in the directory you want to work in,
  then connect from the Claude app or web.

NOTES
  - You must be logged in with a Claude account that has a subscription
  - Run `{{AppMetadata.CommandName}}` first in the directory to accept the workspace trust dialog
  - Worktree mode is still blocked in the current ClawSharp bridge runtime
""";

        await stdout.WriteAsync(help.AsMemory(), cancellationToken);
    }

    private static BridgeCliEntrypointDependencies CreateDefaultDependencies()
    {
        var secureStorage = McpSecureStorageFactory.CreateDefault();
        var oauthTokenSource = new ClaudeAiOAuthTokenSource();
        var clientContextUtilities = new BridgeClientContextUtilities();
        var trustedDeviceTokenSource = new BridgeTrustedDeviceTokenSource(
            new BridgeTrustedDeviceTokenSourceDependencies(
                secureStorage,
                IsGateEnabled: static () => true));
        var worktreeResolver = new GitWorktreePathResolver();
        var settingsBootstrapper = new SettingsBootstrapper();

        IReadOnlyDictionary<string, string?> GetEnvironmentSnapshot()
        {
            Dictionary<string, string?> snapshot = new(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                snapshot[(string)entry.Key] = entry.Value?.ToString();
            }

            return snapshot;
        }

        string? GetAccessToken()
        {
            return BridgeConfigUtilities.GetBridgeAccessToken(
                GetEnvironmentSnapshot(),
                new BridgeConfigDependencies(
                    GetClaudeAiAccessToken: () => oauthTokenSource.ReadToken() ?? secureStorage.Read()?.ClaudeAiOauth?.AccessToken));
        }

        string GetBridgeBaseUrl()
        {
            return BridgeConfigUtilities.GetBridgeBaseUrl(
                GetEnvironmentSnapshot(),
                new BridgeConfigDependencies(
                    GetBaseApiUrl: static () =>
                        Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ??
                        EnvironmentQueryModelHttpClientConfigProvider.DefaultBaseUrl));
        }

        string? GetSessionIngressUrl()
        {
            var environment = GetEnvironmentSnapshot();
            return string.Equals(environment.TryGetValue("USER_TYPE", out var userType) ? userType : null, "ant", StringComparison.Ordinal)
                ? environment.TryGetValue("CLAUDE_BRIDGE_SESSION_INGRESS_URL", out var ingressUrl) ? ingressUrl : null
                : null;
        }

        async Task<string> GetBranchAsync(string dir, CancellationToken cancellationToken)
        {
            var result = await ProcessExecutionUtilities.ExecuteAsync(
                "git",
                ["branch", "--show-current"],
                dir,
                cancellationToken: cancellationToken);
            return result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
        }

        async Task<string?> GetRemoteUrlAsync(string dir, CancellationToken cancellationToken)
        {
            var result = await ProcessExecutionUtilities.ExecuteAsync(
                "git",
                ["config", "--get", "remote.origin.url"],
                dir,
                cancellationToken: cancellationToken);
            var value = result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        bool IsWorktreeAvailable(string dir)
        {
            var result = ProcessExecutionUtilities.ExecuteAsync(
                "git",
                ["rev-parse", "--show-toplevel"],
                dir,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
            return result.ExitCode == 0;
        }

        BridgePointerStoreDependencies CreatePointerDependencies(Action<string>? onDebug = null)
        {
            return new BridgePointerStoreDependencies(
                SessionStoragePaths.GetProjectsDir(),
                onDebug,
                GetWorktreePathsAsync: (dir, token) => worktreeResolver.GetWorktreePathsAsync(dir, token));
        }

        async Task<int> RunRuntimeAsync(
            BridgeRuntimeRequest request,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var settings = (await settingsBootstrapper.LoadAsync(
                request.Startup.Config.Dir,
                cancellationToken)).Settings;
            using var httpClient = new HttpClient();

            BridgeSessionApiDependencies CreateSessionApiDependencies()
            {
                return new BridgeSessionApiDependencies(
                    HttpClient: httpClient,
                    GetAccessToken: GetAccessToken,
                    GetOrganizationUuid: clientContextUtilities.GetCachedOrganizationUuid,
                    GetBaseUrl: () => request.Startup.Config.ApiBaseUrl,
                    GetMainLoopModel: () => MainLoopModelResolver.Resolve(settings.Runtime.Model),
                    ParseGitHubRepository: GitHubRepoPathMappingService.ParseGitHubRepository,
                    OnDebug: log);
            }

            var workerRegistrarDependencies = new CodeSessionWorkerRegistrarDependencies(
                HttpClient: httpClient,
                GetAccessToken: GetAccessToken,
                GetTrustedDeviceToken: trustedDeviceTokenSource.GetTrustedDeviceToken,
                OnDebug: log);

            var api = BridgeApiClient.Create(
                new BridgeApiClientDependencies(
                    BaseUrl: request.Startup.Config.ApiBaseUrl,
                    GetAccessToken: GetAccessToken,
                    RunnerVersion: AppMetadata.DisplayVersion,
                    OnDebug: log,
                    OnAuth401: static _ => Task.FromResult(false),
                    GetTrustedDeviceToken: trustedDeviceTokenSource.GetTrustedDeviceToken),
                httpClient);
            var logger = BridgeHeadlessRuntime.CreateHeadlessBridgeLogger(log);
            var pointerDependencies = CreatePointerDependencies(log);
            var startupCoordinator = new BridgeStartupCoordinator(
                new BridgeStartupDependencies(
                    Api: api,
                    GetBridgeSessionAsync: (sessionId, token) =>
                        BridgeSessionApiClient.GetBridgeSessionAsync(
                            CreateSessionApiDependencies(),
                            sessionId,
                            token),
                    CreateSessionAsync: (environmentId, title, token) =>
                        BridgeSessionApiClient.CreateBridgeSessionAsync(
                            CreateSessionApiDependencies(),
                            environmentId,
                            [],
                            request.Startup.Config.GitRepoUrl,
                            request.Startup.Config.Branch,
                            title,
                            request.Startup.PermissionMode,
                            token),
                    WriteBridgePointerAsync: (dir, sessionId, environmentId, token) =>
                        BridgePointerStore.WriteBridgePointerAsync(
                            pointerDependencies,
                            dir,
                            new BridgePointer(sessionId, environmentId, "standalone"),
                            token),
                    ClearBridgePointerAsync: (dir, token) =>
                        BridgePointerStore.ClearBridgePointerAsync(pointerDependencies, dir, token),
                    OnDebug: log));
            var shutdownCoordinator = new BridgeShutdownCoordinator(
                new BridgeShutdownDependencies(
                    Api: api,
                    Logger: logger,
                    ClearBridgePointerAsync: (dir, token) =>
                        BridgePointerStore.ClearBridgePointerAsync(pointerDependencies, dir, token),
                    OnDebug: log));
            var childLaunch = ResolveChildProcessLaunch();
            var spawner = new ProcessSessionSpawner(
                new ProcessSessionSpawnerDependencies(
                    ExecutablePath: childLaunch.ExecutablePath,
                    ExecutableArgumentsPrefix: childLaunch.ArgumentPrefix,
                    Verbose: request.Startup.Config.Verbose,
                    Sandbox: request.Startup.Config.Sandbox,
                    DebugFile: request.Startup.Config.DebugFile,
                    PermissionMode: request.Startup.PermissionMode,
                    OnDebug: log));
            var runtimeCoordinator = new BridgeRuntimeCoordinator(
                new BridgeRuntimeDependencies(
                    StartupCoordinator: startupCoordinator,
                    ShutdownCoordinator: shutdownCoordinator,
                    Logger: logger,
                    RunLoopAsync: (config, environmentId, environmentSecret, initialSessionId, token) =>
                        RunBridgeLoopAsync(
                            api,
                            spawner,
                            logger,
                            workerRegistrarDependencies,
                            CreateSessionApiDependencies,
                            config,
                            environmentId,
                            environmentSecret,
                            initialSessionId,
                            log,
                            token),
                    OnDebug: log));

            await runtimeCoordinator.RunAsync(request, cancellationToken);
            return 0;
        }

        return new BridgeCliEntrypointDependencies(
            GetCurrentDirectory: Directory.GetCurrentDirectory,
            CheckHasTrustDialogAcceptedAsync: clientContextUtilities.CheckHasTrustDialogAcceptedAsync,
            GetAccessToken: GetAccessToken,
            GetBridgeBaseUrl: GetBridgeBaseUrl,
            GetSessionIngressUrl: GetSessionIngressUrl,
            GetBranchAsync: GetBranchAsync,
            GetRemoteUrlAsync: GetRemoteUrlAsync,
            IsWorktreeAvailable: IsWorktreeAvailable,
            ReadBridgePointerAcrossWorktreesAsync: (dir, token) =>
                BridgePointerStore.ReadBridgePointerAcrossWorktreesAsync(
                    CreatePointerDependencies(),
                    dir,
                    token),
            ClearBridgePointerAsync: (dir, token) =>
                BridgePointerStore.ClearBridgePointerAsync(
                    CreatePointerDependencies(),
                    dir,
                    token),
            RunRuntimeAsync: RunRuntimeAsync);
    }

    private static Task<BridgeRuntimeLoopResult> RunBridgeLoopAsync(
        IBridgeApiClient api,
        ISessionSpawner spawner,
        IBridgeLogger logger,
        CodeSessionWorkerRegistrarDependencies workerRegistrarDependencies,
        Func<BridgeSessionApiDependencies> createSessionApiDependencies,
        BridgeConfig config,
        string environmentId,
        string environmentSecret,
        string? initialSessionId,
        Action<string> onDebug,
        CancellationToken cancellationToken)
    {
        return RunBridgeLoopCoreAsync(
            api,
            spawner,
            logger,
            workerRegistrarDependencies,
            createSessionApiDependencies,
            config,
            environmentId,
            environmentSecret,
            initialSessionId,
            onDebug,
            cancellationToken);
    }

    private static Task WatchSessionAsync(
        string sessionId,
        ISessionHandle handle,
        ConcurrentDictionary<string, DateTimeOffset> sessionStartTimes,
        BridgeSessionCompletionCoordinator completionCoordinator,
        BridgeSessionCompletionState completionState,
        BridgeConfig config,
        string environmentId,
        CancellationToken loopToken,
        Action<string> onDebug)
    {
        return WatchSessionCoreAsync(
            sessionId,
            handle,
            sessionStartTimes,
            completionCoordinator,
            completionState,
            config,
            environmentId,
            loopToken,
            onDebug);
    }

    private static Task<string?> FetchSessionTitleAsync(
        BridgeSessionApiDependencies dependencies,
        string sessionId,
        CancellationToken cancellationToken)
    {
        return FetchSessionTitleCoreAsync(dependencies, sessionId, cancellationToken);
    }

    private static (string ExecutablePath, IReadOnlyList<string> ArgumentPrefix) ResolveChildProcessLaunch()
    {
        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("Current process path is unavailable.");
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath) &&
            entryAssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(executablePath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (executablePath, [entryAssemblyPath]);
        }

        return (executablePath, []);
    }

    private static async Task<BridgeRuntimeLoopResult> RunBridgeLoopCoreAsync(
        IBridgeApiClient api,
        ISessionSpawner spawner,
        IBridgeLogger logger,
        CodeSessionWorkerRegistrarDependencies workerRegistrarDependencies,
        Func<BridgeSessionApiDependencies> createSessionApiDependencies,
        BridgeConfig config,
        string environmentId,
        string environmentSecret,
        string? initialSessionId,
        Action<string> onDebug,
        CancellationToken cancellationToken)
    {
        using var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loopToken = loopCancellation.Token;
        var capacityWake = new CapacityWake(loopToken);
        var activeSessions = new ConcurrentDictionary<string, ISessionHandle>(StringComparer.Ordinal);
        var sessionWorkIds = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var sessionIngressTokens = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var sessionCompatIds = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var sessionStartTimes = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var sessionWorktrees = new ConcurrentDictionary<string, BridgeSessionWorktree>(StringComparer.Ordinal);
        var completedWorkIds = new ConcurrentSet<string>(StringComparer.Ordinal);
        var timedOutSessions = new ConcurrentSet<string>(StringComparer.Ordinal);
        var titledSessions = new ConcurrentSet<string>(StringComparer.Ordinal);
        var v2Sessions = new ConcurrentSet<string>(StringComparer.Ordinal);
        var pendingCleanups = new ConcurrentSet<Task>();
        var watchedSessions = new ConcurrentSet<string>(StringComparer.Ordinal);
        var stopWorkRetryDependencies = new BridgeStopWorkRetryDependencies(api, logger, onDebug);
        var completionState = new BridgeSessionCompletionState(
            ActiveSessions: activeSessions,
            SessionStartTimes: sessionStartTimes,
            SessionWorkIds: sessionWorkIds,
            SessionIngressTokens: sessionIngressTokens,
            SessionCompatIds: sessionCompatIds,
            SessionWorktrees: sessionWorktrees,
            TimedOutSessions: timedOutSessions,
            TitledSessions: titledSessions,
            V2Sessions: v2Sessions,
            PendingCleanups: pendingCleanups);
        var completionCoordinator = new BridgeSessionCompletionCoordinator(
            new BridgeSessionCompletionDependencies(
                Api: api,
                Logger: logger,
                CapacityWake: capacityWake,
                StopWorkRetryDependencies: stopWorkRetryDependencies,
                AbortLoop: _ => loopCancellation.Cancel(),
                OnDebug: onDebug));
        var dispatchCoordinator = new BridgeWorkDispatchCoordinator(
            new BridgeWorkDispatchDependencies(
                Api: api,
                Logger: logger,
                Spawner: spawner,
                StopWorkRetryDependencies: stopWorkRetryDependencies,
                RegisterWorkerAsync: async (sdkUrl, token) =>
                    (await CodeSessionWorkerRegistrar.RegisterWorkerAsync(
                        workerRegistrarDependencies,
                        sdkUrl,
                        token)).WorkerEpoch,
                FetchSessionTitleAsync: (sessionId, token) =>
                    FetchSessionTitleAsync(createSessionApiDependencies(), sessionId, token),
                UpdateBridgeSessionTitleAsync: (sessionId, title, token) =>
                    BridgeSessionApiClient.UpdateBridgeSessionTitleAsync(
                        createSessionApiDependencies(),
                        sessionId,
                        title,
                        token),
                OnSessionSpawnedAsync: (sessionId, handle, _, _, _, _, _, _) =>
                {
                    if (watchedSessions.Add(sessionId))
                    {
                        _ = WatchSessionAsync(
                            sessionId,
                            handle,
                            sessionStartTimes,
                            completionCoordinator,
                            completionState,
                            config,
                            environmentId,
                            loopToken,
                            onDebug);
                    }

                    return Task.CompletedTask;
                },
                OnDebug: onDebug));

        var pollResult = await BridgePollLoopRunner.RunAsync(
            new BridgePollLoopDependencies(
                Api: api,
                Logger: logger,
                CapacityWake: capacityWake,
                GetPollConfig: static () => BridgePollConfig.Default,
                GetActiveSessionCount: () => activeSessions.Count,
                OnWorkAsync: (work, atCapacityBeforeSwitch, _, token) =>
                    dispatchCoordinator.DispatchAsync(
                        new BridgeWorkDispatchRequest(
                            Config: config,
                            EnvironmentId: environmentId,
                            Work: work,
                            AtCapacityBeforeSwitch: atCapacityBeforeSwitch,
                            State: new BridgeWorkDispatchState(
                                ActiveSessions: activeSessions,
                                SessionWorkIds: sessionWorkIds,
                                SessionIngressTokens: sessionIngressTokens,
                                SessionCompatIds: sessionCompatIds,
                                SessionStartTimes: sessionStartTimes,
                                SessionWorktrees: sessionWorktrees,
                                CompletedWorkIds: completedWorkIds,
                                V2Sessions: v2Sessions,
                                TitledSessions: titledSessions),
                            InitialSessionId: initialSessionId),
                        token),
                HeartbeatActiveWorkItemsAsync: token =>
                    BridgeHeartbeatCoordinator.HeartbeatActiveWorkItemsAsync(
                        new BridgeHeartbeatDependencies(api, logger, onDebug),
                        environmentId,
                        sessionWorkIds
                            .Select(pair =>
                                sessionIngressTokens.TryGetValue(pair.Key, out var tokenText)
                                    ? new BridgeHeartbeatWorkItem(pair.Key, pair.Value, tokenText)
                                    : null)
                            .Where(static item => item is not null)!
                            .Select(static item => item!),
                        token),
                OnDebug: onDebug),
            environmentId,
            environmentSecret,
            config.MaxSessions,
            loopToken);

        var shutdownWorktrees = sessionWorktrees.ToDictionary(
            static pair => pair.Key,
            static pair => new BridgeShutdownWorktree(
                pair.Value.WorktreePath,
                pair.Value.WorktreeBranch,
                pair.Value.GitRoot,
                pair.Value.HookBased),
            StringComparer.Ordinal);

        return new BridgeRuntimeLoopResult(
            FatalExit: pollResult.FatalExit,
            ActiveSessions: activeSessions.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            SessionWorkIds: sessionWorkIds.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            SessionCompatIds: sessionCompatIds.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            SessionWorktrees: shutdownWorktrees,
            PendingCleanups: pendingCleanups.ToArray());
    }

    private static async Task WatchSessionCoreAsync(
        string sessionId,
        ISessionHandle handle,
        ConcurrentDictionary<string, DateTimeOffset> sessionStartTimes,
        BridgeSessionCompletionCoordinator completionCoordinator,
        BridgeSessionCompletionState completionState,
        BridgeConfig config,
        string environmentId,
        CancellationToken loopToken,
        Action<string> onDebug)
    {
        try
        {
            var rawStatus = await handle.Done.ConfigureAwait(false);
            var startTime = sessionStartTimes.TryGetValue(sessionId, out var knownStartTime)
                ? knownStartTime
                : DateTimeOffset.UtcNow;
            await completionCoordinator.CompleteAsync(
                new BridgeSessionCompletionRequest(
                    Config: config,
                    EnvironmentId: environmentId,
                    SessionId: sessionId,
                    StartTime: startTime,
                    Handle: handle,
                    RawStatus: rawStatus,
                    State: completionState,
                    LoopAborted: loopToken.IsCancellationRequested),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            onDebug($"[bridge:session] completion watcher failed for {sessionId}: {error.Message}");
        }
    }

    private static async Task<string?> FetchSessionTitleCoreAsync(
        BridgeSessionApiDependencies dependencies,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = await BridgeSessionApiClient.GetBridgeSessionAsync(
            dependencies,
            sessionId,
            cancellationToken);
        return string.IsNullOrWhiteSpace(session?.Title) ? null : session.Title;
    }

    private sealed class ConcurrentSet<T>(IEqualityComparer<T>? comparer = null) : ISet<T>
        where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> _dictionary = new(comparer ?? EqualityComparer<T>.Default);

        public int Count => _dictionary.Count;
        public bool IsReadOnly => false;
        public bool Add(T item) => _dictionary.TryAdd(item, 0);
        void ICollection<T>.Add(T item) => Add(item);
        public void ExceptWith(IEnumerable<T> other)
        {
            foreach (var item in other)
            {
                _dictionary.TryRemove(item, out _);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            var allowed = new HashSet<T>(other, _dictionary.Comparer);
            foreach (var item in _dictionary.Keys)
            {
                if (!allowed.Contains(item))
                {
                    _dictionary.TryRemove(item, out _);
                }
            }
        }

        public bool IsProperSubsetOf(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => new HashSet<T>(this, _dictionary.Comparer).SetEquals(other);
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            foreach (var item in other)
            {
                if (!_dictionary.TryRemove(item, out _))
                {
                    _dictionary.TryAdd(item, 0);
                }
            }
        }

        public void UnionWith(IEnumerable<T> other)
        {
            foreach (var item in other)
            {
                _dictionary.TryAdd(item, 0);
            }
        }

        public void Clear() => _dictionary.Clear();
        public bool Contains(T item) => _dictionary.ContainsKey(item);
        public void CopyTo(T[] array, int arrayIndex) => _dictionary.Keys.CopyTo(array, arrayIndex);
        public bool Remove(T item) => _dictionary.TryRemove(item, out _);
        public IEnumerator<T> GetEnumerator() => _dictionary.Keys.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public T[] ToArray() => _dictionary.Keys.ToArray();
    }
}
