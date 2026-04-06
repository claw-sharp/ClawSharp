// TS origin: ./bridge/bridgeMain.ts
namespace ClawSharp.Bridge;

public sealed record BridgeSessionWorktree(
    string WorktreePath,
    string? WorktreeBranch = null,
    string? GitRoot = null,
    bool? HookBased = null);

public sealed record BridgeWorkDispatchState(
    IDictionary<string, ISessionHandle> ActiveSessions,
    IDictionary<string, string> SessionWorkIds,
    IDictionary<string, string> SessionIngressTokens,
    IDictionary<string, string> SessionCompatIds,
    IDictionary<string, DateTimeOffset> SessionStartTimes,
    IDictionary<string, BridgeSessionWorktree> SessionWorktrees,
    ISet<string> CompletedWorkIds,
    ISet<string> V2Sessions,
    ISet<string> TitledSessions);

public sealed record BridgeWorkDispatchResult(
    bool Handled,
    bool SpawnedSession = false,
    bool RefreshedExistingSession = false,
    bool SkippedCompletedWork = false,
    bool StopWorkScheduled = false,
    bool AtCapacityRefused = false,
    string? SessionId = null,
    string? CompatSessionId = null,
    bool? UseCcrV2 = null,
    long? WorkerEpoch = null);

public sealed record BridgeWorkDispatchDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    ISessionSpawner Spawner,
    BridgeStopWorkRetryDependencies StopWorkRetryDependencies,
    Func<string, CancellationToken, Task<long>>? RegisterWorkerAsync = null,
    Func<string, CancellationToken, Task<BridgeSessionWorktree>>? CreateAgentWorktreeAsync = null,
    Func<BridgeSessionWorktree, CancellationToken, Task>? RemoveAgentWorktreeAsync = null,
    Func<string, bool>? IsEnvTruthy = null,
    Func<double, CancellationToken, Task>? SleepAsync = null,
    Func<DateTimeOffset>? GetNow = null,
    Func<string, string, string>? GetRemoteSessionUrl = null,
    Func<string, string>? DeriveSessionTitle = null,
    Func<string, string, CancellationToken, Task>? UpdateBridgeSessionTitleAsync = null,
    Func<string, CancellationToken, Task<string?>>? FetchSessionTitleAsync = null,
    Func<string, ISessionHandle, string, BridgeWorkSecret, bool, long?, BridgeSessionWorktree?, CancellationToken, Task>? OnSessionSpawnedAsync = null,
    Func<string, ISessionHandle, string, BridgeWorkSecret, CancellationToken, Task>? OnExistingSessionRefreshedAsync = null,
    Action<string>? OnDebug = null);

public sealed record BridgeWorkDispatchRequest(
    BridgeConfig Config,
    string EnvironmentId,
    BridgeWorkResponse Work,
    bool AtCapacityBeforeSwitch,
    BridgeWorkDispatchState State,
    string? InitialSessionId = null,
    double StopWorkBaseDelayMs = 1000d);

public sealed class BridgeWorkDispatchCoordinator(BridgeWorkDispatchDependencies dependencies)
{
    public async Task<BridgeWorkDispatchResult> DispatchAsync(
        BridgeWorkDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sleepAsync = dependencies.SleepAsync ?? DefaultSleepAsync;
        var getNow = dependencies.GetNow ?? (() => DateTimeOffset.UtcNow);
        var isEnvTruthy = dependencies.IsEnvTruthy ?? DefaultIsEnvTruthy;
        var getRemoteSessionUrl = dependencies.GetRemoteSessionUrl ?? BridgeProductUrls.GetRemoteSessionUrl;

        if (request.State.CompletedWorkIds.Contains(request.Work.Id))
        {
            dependencies.OnDebug?.Invoke($"[bridge:work] Skipping already-completed workId={request.Work.Id}");
            return new BridgeWorkDispatchResult(Handled: true, SkippedCompletedWork: true);
        }

        BridgeWorkSecret secret;
        try
        {
            secret = BridgeWorkSecretUtilities.DecodeWorkSecret(request.Work.Secret);
        }
        catch (Exception error)
        {
            dependencies.Logger.LogError($"Failed to decode work secret for workId={request.Work.Id}: {error.Message}");
            request.State.CompletedWorkIds.Add(request.Work.Id);
            await BridgeRetryUtilities.StopWorkWithRetryAsync(
                dependencies.StopWorkRetryDependencies,
                request.EnvironmentId,
                request.Work.Id,
                request.StopWorkBaseDelayMs,
                cancellationToken);
            return new BridgeWorkDispatchResult(Handled: true, StopWorkScheduled: true);
        }

        async Task AcknowledgeWorkAsync()
        {
            dependencies.OnDebug?.Invoke($"[bridge:work] Acknowledging workId={request.Work.Id}");
            try
            {
                await dependencies.Api.AcknowledgeWorkAsync(
                    request.EnvironmentId,
                    request.Work.Id,
                    secret.SessionIngressToken,
                    cancellationToken);
            }
            catch (Exception error)
            {
                dependencies.OnDebug?.Invoke($"[bridge:work] Acknowledge failed workId={request.Work.Id}: {error.Message}");
            }
        }

        switch (request.Work.Data.Type)
        {
            case BridgeWorkDataType.Healthcheck:
                await AcknowledgeWorkAsync();
                dependencies.OnDebug?.Invoke("[bridge:work] Healthcheck received");
                dependencies.Logger.LogVerbose("Healthcheck received");
                return new BridgeWorkDispatchResult(Handled: true);

            case BridgeWorkDataType.Session:
                return await DispatchSessionWorkAsync(
                    request,
                    secret,
                    sleepAsync,
                    getNow,
                    isEnvTruthy,
                    getRemoteSessionUrl,
                    cancellationToken,
                    AcknowledgeWorkAsync);

            default:
                await AcknowledgeWorkAsync();
                dependencies.OnDebug?.Invoke($"[bridge:work] Unknown work type: {request.Work.Type}, skipping");
                return new BridgeWorkDispatchResult(Handled: true);
        }
    }

    private async Task<BridgeWorkDispatchResult> DispatchSessionWorkAsync(
        BridgeWorkDispatchRequest request,
        BridgeWorkSecret secret,
        Func<double, CancellationToken, Task> sleepAsync,
        Func<DateTimeOffset> getNow,
        Func<string, bool> isEnvTruthy,
        Func<string, string, string> getRemoteSessionUrl,
        CancellationToken cancellationToken,
        Func<Task> acknowledgeWorkAsync)
    {
        var sessionId = request.Work.Data.Id;
        try
        {
            BridgeApiErrorUtilities.ValidateBridgeId(sessionId, "session_id");
        }
        catch
        {
            await acknowledgeWorkAsync();
            dependencies.Logger.LogError($"Invalid session_id received: {sessionId}");
            return new BridgeWorkDispatchResult(Handled: true);
        }

        if (request.State.ActiveSessions.TryGetValue(sessionId, out var existingHandle))
        {
            existingHandle.UpdateAccessToken(secret.SessionIngressToken);
            request.State.SessionIngressTokens[sessionId] = secret.SessionIngressToken;
            request.State.SessionWorkIds[sessionId] = request.Work.Id;
            dependencies.OnDebug?.Invoke($"[bridge:work] Updated access token for existing sessionId={sessionId} workId={request.Work.Id}");
            if (dependencies.OnExistingSessionRefreshedAsync is not null)
            {
                await dependencies.OnExistingSessionRefreshedAsync(
                    sessionId,
                    existingHandle,
                    request.Work.Id,
                    secret,
                    cancellationToken);
            }

            await acknowledgeWorkAsync();
            return new BridgeWorkDispatchResult(
                Handled: true,
                RefreshedExistingSession: true,
                SessionId: sessionId,
                CompatSessionId: request.State.SessionCompatIds.TryGetValue(sessionId, out var existingCompatId)
                    ? existingCompatId
                    : BridgeSessionIdCompat.ToCompatSessionId(sessionId));
        }

        if (request.State.ActiveSessions.Count >= request.Config.MaxSessions)
        {
            dependencies.OnDebug?.Invoke($"[bridge:work] At capacity ({request.State.ActiveSessions.Count}/{request.Config.MaxSessions}), cannot spawn new session for workId={request.Work.Id}");
            return new BridgeWorkDispatchResult(Handled: true, AtCapacityRefused: true);
        }

        await acknowledgeWorkAsync();

        string sdkUrl;
        var useCcrV2 = false;
        long? workerEpoch = null;
        if (secret.UseCodeSessions == true || isEnvTruthy("CLAUDE_BRIDGE_USE_CCR_V2"))
        {
            sdkUrl = BridgeWorkSecretUtilities.BuildCcrV2SdkUrl(request.Config.ApiBaseUrl, sessionId);
            if (dependencies.RegisterWorkerAsync is null)
            {
                throw new InvalidOperationException("CCR v2 worker registration callback is required to dispatch use_code_sessions bridge work.");
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    workerEpoch = await dependencies.RegisterWorkerAsync(sdkUrl, cancellationToken);
                    useCcrV2 = true;
                    dependencies.OnDebug?.Invoke($"[bridge:session] CCR v2: registered worker sessionId={sessionId} epoch={workerEpoch} attempt={attempt}");
                    break;
                }
                catch (Exception error)
                {
                    if (attempt < 2)
                    {
                        dependencies.OnDebug?.Invoke($"[bridge:session] CCR v2: registerWorker attempt {attempt} failed, retrying: {error.Message}");
                        await sleepAsync(2_000d, cancellationToken);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    dependencies.Logger.LogError($"CCR v2 worker registration failed for session {sessionId}: {error.Message}");
                    request.State.CompletedWorkIds.Add(request.Work.Id);
                    await BridgeRetryUtilities.StopWorkWithRetryAsync(
                        dependencies.StopWorkRetryDependencies,
                        request.EnvironmentId,
                        request.Work.Id,
                        request.StopWorkBaseDelayMs,
                        cancellationToken);
                    return new BridgeWorkDispatchResult(
                        Handled: true,
                        StopWorkScheduled: true,
                        SessionId: sessionId);
                }
            }

            if (!useCcrV2)
            {
                return new BridgeWorkDispatchResult(Handled: true, SessionId: sessionId);
            }
        }
        else
        {
            sdkUrl = BridgeWorkSecretUtilities.BuildSdkUrl(request.Config.SessionIngressUrl, sessionId);
        }

        var sessionDir = request.Config.Dir;
        var sessionWorktree = default(BridgeSessionWorktree);
        if (request.Config.SpawnMode == SpawnMode.Worktree &&
            (request.InitialSessionId is null || !BridgeWorkSecretUtilities.SameSessionId(sessionId, request.InitialSessionId)))
        {
            if (dependencies.CreateAgentWorktreeAsync is null)
            {
                throw new InvalidOperationException("Worktree creation callback is required to dispatch bridge worktree sessions.");
            }

            try
            {
                sessionWorktree = await dependencies.CreateAgentWorktreeAsync(
                    $"bridge-{SessionRunnerUtilities.SafeFilenameId(sessionId)}",
                    cancellationToken);
                request.State.SessionWorktrees[sessionId] = sessionWorktree;
                sessionDir = sessionWorktree.WorktreePath;
                dependencies.OnDebug?.Invoke($"[bridge:session] Created worktree for sessionId={sessionId} at {sessionWorktree.WorktreePath}");
            }
            catch (Exception error)
            {
                dependencies.Logger.LogError($"Failed to create worktree for session {sessionId}: {error.Message}");
                request.State.CompletedWorkIds.Add(request.Work.Id);
                await BridgeRetryUtilities.StopWorkWithRetryAsync(
                    dependencies.StopWorkRetryDependencies,
                    request.EnvironmentId,
                    request.Work.Id,
                    request.StopWorkBaseDelayMs,
                    cancellationToken);
                return new BridgeWorkDispatchResult(
                    Handled: true,
                    StopWorkScheduled: true,
                    SessionId: sessionId);
            }
        }

        dependencies.OnDebug?.Invoke($"[bridge:session] Spawning sessionId={sessionId} sdkUrl={sdkUrl}");
        var compatSessionId = BridgeSessionIdCompat.ToCompatSessionId(sessionId);
        ISessionHandle handle;
        try
        {
            handle = dependencies.Spawner.Spawn(
                new SessionSpawnOptions(
                    SessionId: sessionId,
                    SdkUrl: sdkUrl,
                    AccessToken: secret.SessionIngressToken,
                    UseCcrV2: useCcrV2,
                    WorkerEpoch: workerEpoch,
                    OnFirstUserMessage: CreateOnFirstUserMessageCallback(request, compatSessionId)),
                sessionDir);
        }
        catch (Exception error)
        {
            dependencies.Logger.LogError($"Failed to spawn session {sessionId}: {error.Message}");
            await CleanupFailedSpawnAsync(request, sessionId, sessionWorktree, cancellationToken);
            return new BridgeWorkDispatchResult(
                Handled: true,
                StopWorkScheduled: true,
                SessionId: sessionId,
                CompatSessionId: compatSessionId);
        }

        request.State.ActiveSessions[sessionId] = handle;
        request.State.SessionWorkIds[sessionId] = request.Work.Id;
        request.State.SessionIngressTokens[sessionId] = secret.SessionIngressToken;
        request.State.SessionCompatIds[sessionId] = compatSessionId;
        request.State.SessionStartTimes[sessionId] = getNow();
        if (useCcrV2)
        {
            request.State.V2Sessions.Add(sessionId);
        }

        dependencies.Logger.LogSessionStart(sessionId, $"Session {sessionId}");
        dependencies.Logger.AddSession(compatSessionId, getRemoteSessionUrl(compatSessionId, request.Config.SessionIngressUrl));
        dependencies.Logger.SetAttached(compatSessionId);

        StartFetchSessionTitleTask(request, sessionId, compatSessionId);
        if (dependencies.OnSessionSpawnedAsync is not null)
        {
            await dependencies.OnSessionSpawnedAsync(
                sessionId,
                handle,
                request.Work.Id,
                secret,
                useCcrV2,
                workerEpoch,
                sessionWorktree,
                cancellationToken);
        }

        return new BridgeWorkDispatchResult(
            Handled: true,
            SpawnedSession: true,
            SessionId: sessionId,
            CompatSessionId: compatSessionId,
            UseCcrV2: useCcrV2,
            WorkerEpoch: workerEpoch);
    }

    private Action<string>? CreateOnFirstUserMessageCallback(BridgeWorkDispatchRequest request, string compatSessionId)
    {
        if (dependencies.DeriveSessionTitle is null)
        {
            return null;
        }

        return text =>
        {
            if (request.State.TitledSessions.Contains(compatSessionId))
            {
                return;
            }

            request.State.TitledSessions.Add(compatSessionId);
            var title = dependencies.DeriveSessionTitle(text);
            dependencies.Logger.SetSessionTitle(compatSessionId, title);
            dependencies.OnDebug?.Invoke($"[bridge:title] derived title for {compatSessionId}: {title}");
            if (dependencies.UpdateBridgeSessionTitleAsync is not null)
            {
                _ = dependencies.UpdateBridgeSessionTitleAsync(
                    compatSessionId,
                    title,
                    CancellationToken.None).ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted && task.Exception is not null)
                        {
                            dependencies.OnDebug?.Invoke($"[bridge:title] failed to update title for {compatSessionId}: {task.Exception.GetBaseException().Message}");
                        }
                    },
                    TaskScheduler.Default);
            }
        };
    }

    private void StartFetchSessionTitleTask(BridgeWorkDispatchRequest request, string sessionId, string compatSessionId)
    {
        if (dependencies.FetchSessionTitleAsync is null)
        {
            return;
        }

        _ = dependencies.FetchSessionTitleAsync(compatSessionId, CancellationToken.None).ContinueWith(
            task =>
            {
                if (task.Status == TaskStatus.RanToCompletion &&
                    !string.IsNullOrWhiteSpace(task.Result) &&
                    request.State.ActiveSessions.ContainsKey(sessionId))
                {
                    request.State.TitledSessions.Add(compatSessionId);
                    dependencies.Logger.SetSessionTitle(compatSessionId, task.Result!);
                    dependencies.OnDebug?.Invoke($"[bridge:title] server title for {compatSessionId}: {task.Result}");
                }
            },
            TaskScheduler.Default);
    }

    private async Task CleanupFailedSpawnAsync(
        BridgeWorkDispatchRequest request,
        string sessionId,
        BridgeSessionWorktree? sessionWorktree,
        CancellationToken cancellationToken)
    {
        if (sessionWorktree is not null)
        {
            request.State.SessionWorktrees.Remove(sessionId);
            if (dependencies.RemoveAgentWorktreeAsync is not null)
            {
                try
                {
                    await dependencies.RemoveAgentWorktreeAsync(sessionWorktree, cancellationToken);
                }
                catch (Exception cleanupError)
                {
                    dependencies.Logger.LogVerbose($"Failed to remove worktree {sessionWorktree.WorktreePath}: {cleanupError.Message}");
                }
            }
        }

        request.State.CompletedWorkIds.Add(request.Work.Id);
        await BridgeRetryUtilities.StopWorkWithRetryAsync(
            dependencies.StopWorkRetryDependencies,
            request.EnvironmentId,
            request.Work.Id,
            request.StopWorkBaseDelayMs,
            cancellationToken);
    }

    private static Task DefaultSleepAsync(double delayMs, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);

    private static bool DefaultIsEnvTruthy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !string.Equals(value, "0", StringComparison.Ordinal) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }
}
