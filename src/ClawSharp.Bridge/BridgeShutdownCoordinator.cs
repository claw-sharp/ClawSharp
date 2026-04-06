// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgeShutdownWorktree(
    string WorktreePath,
    string? WorktreeBranch = null,
    string? GitRoot = null,
    bool? HookBased = null);

public sealed record BridgeShutdownRequest(
    BridgeConfig Config,
    string EnvironmentId,
    bool FatalExit,
    string? InitialSessionId,
    IReadOnlyDictionary<string, ISessionHandle> ActiveSessions,
    IReadOnlyDictionary<string, string> SessionWorkIds,
    IReadOnlyDictionary<string, string> SessionCompatIds,
    IReadOnlyDictionary<string, BridgeShutdownWorktree>? SessionWorktrees = null,
    IReadOnlyCollection<Task>? PendingCleanups = null,
    bool AllowResumeOnShutdown = false,
    double ShutdownGraceMs = 30_000);

public sealed record BridgeShutdownResult(
    bool ResumableShutdown,
    int ArchivedSessionCount,
    bool DeregisterAttempted,
    bool PointerCleared);

public sealed record BridgeShutdownDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    Func<string, CancellationToken, Task> ClearBridgePointerAsync,
    Func<BridgeShutdownWorktree, CancellationToken, Task>? RemoveWorktreeAsync = null,
    Func<double, CancellationToken, Task>? SleepAsync = null,
    Action<string>? OnDebug = null);

public sealed class BridgeShutdownCoordinator
{
    private readonly BridgeShutdownDependencies _dependencies;

    public BridgeShutdownCoordinator(BridgeShutdownDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public async Task<BridgeShutdownResult> ShutdownAsync(
        BridgeShutdownRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Config);
        ArgumentNullException.ThrowIfNull(request.ActiveSessions);
        ArgumentNullException.ThrowIfNull(request.SessionWorkIds);
        ArgumentNullException.ThrowIfNull(request.SessionCompatIds);

        var sleepAsync = _dependencies.SleepAsync ??
                         ((delay, token) => Task.Delay(TimeSpan.FromMilliseconds(delay), token));

        var sessionsToArchive = new HashSet<string>(request.ActiveSessions.Keys, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(request.InitialSessionId))
        {
            sessionsToArchive.Add(request.InitialSessionId);
        }

        if (request.ActiveSessions.Count > 0)
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:shutdown] Shutting down {request.ActiveSessions.Count} active session(s)");
            _dependencies.Logger.LogStatus(
                $"Shutting down {request.ActiveSessions.Count} active session(s)…");

            foreach (var (sessionId, handle) in request.ActiveSessions)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:shutdown] Sending SIGTERM to sessionId={sessionId}");
                handle.Kill();
            }

            using var graceCancellation = new CancellationTokenSource();
            var completedTasks = Task.WhenAll(
                request.ActiveSessions.Values.Select(async handle =>
                {
                    try
                    {
                        await handle.Done;
                    }
                    catch
                    {
                    }
                }));
            var graceDelay = sleepAsync(request.ShutdownGraceMs, graceCancellation.Token);
            await Task.WhenAny(completedTasks, graceDelay);
            graceCancellation.Cancel();

            foreach (var (sessionId, handle) in request.ActiveSessions)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:shutdown] Force-killing stuck sessionId={sessionId}");
                handle.ForceKill();
            }

            if (request.SessionWorktrees is { Count: > 0 } && _dependencies.RemoveWorktreeAsync is not null)
            {
                var worktrees = request.SessionWorktrees.Values.ToArray();
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:shutdown] Cleaning up {worktrees.Length} worktree(s)");
                await Task.WhenAll(
                    worktrees.Select(worktree => RemoveWorktreeIgnoringErrorsAsync(worktree, cancellationToken)));
            }

            await Task.WhenAll(
                request.SessionWorkIds.Select(pair => StopWorkIgnoringErrorsAsync(
                    request.EnvironmentId,
                    pair.Key,
                    pair.Value,
                    cancellationToken)));
        }

        if (request.PendingCleanups is { Count: > 0 })
        {
            await Task.WhenAll(request.PendingCleanups.Select(async cleanup =>
            {
                try
                {
                    await cleanup;
                }
                catch
                {
                }
            }));
        }

        if (request.AllowResumeOnShutdown &&
            request.Config.SpawnMode == SpawnMode.SingleSession &&
            !string.IsNullOrEmpty(request.InitialSessionId) &&
            !request.FatalExit)
        {
            _dependencies.Logger.LogStatus(
                $"Resume this session by running `{AppMetadata.RemoteControlCommand} --continue`");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:shutdown] Skipping archive+deregister to allow resume of session {request.InitialSessionId}");
            return new BridgeShutdownResult(
                ResumableShutdown: true,
                ArchivedSessionCount: 0,
                DeregisterAttempted: false,
                PointerCleared: false);
        }

        if (sessionsToArchive.Count > 0)
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:shutdown] Archiving {sessionsToArchive.Count} session(s)");
            await Task.WhenAll(
                sessionsToArchive.Select(sessionId => ArchiveSessionIgnoringErrorsAsync(
                    request.SessionCompatIds.TryGetValue(sessionId, out var compatId)
                        ? compatId
                        : BridgeSessionIdCompat.ToCompatSessionId(sessionId),
                    sessionId,
                    cancellationToken)));
        }

        var deregisterAttempted = false;
        try
        {
            deregisterAttempted = true;
            await _dependencies.Api.DeregisterEnvironmentAsync(request.EnvironmentId, cancellationToken);
            _dependencies.OnDebug?.Invoke(
                "[bridge:shutdown] Environment deregistered, bridge offline");
            _dependencies.Logger.LogVerbose("Environment deregistered.");
        }
        catch (Exception error)
        {
            _dependencies.Logger.LogVerbose(
                $"Failed to deregister environment: {error.Message}");
        }

        await _dependencies.ClearBridgePointerAsync(request.Config.Dir, cancellationToken);
        _dependencies.Logger.LogVerbose("Environment offline.");

        return new BridgeShutdownResult(
            ResumableShutdown: false,
            ArchivedSessionCount: sessionsToArchive.Count,
            DeregisterAttempted: deregisterAttempted,
            PointerCleared: true);
    }

    private async Task RemoveWorktreeIgnoringErrorsAsync(
        BridgeShutdownWorktree worktree,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dependencies.RemoveWorktreeAsync!(worktree, cancellationToken);
        }
        catch
        {
        }
    }

    private async Task StopWorkIgnoringErrorsAsync(
        string environmentId,
        string sessionId,
        string workId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dependencies.Api.StopWorkAsync(environmentId, workId, true, cancellationToken);
        }
        catch (Exception error)
        {
            _dependencies.Logger.LogVerbose(
                $"Failed to stop work {workId} for session {sessionId}: {error.Message}");
        }
    }

    private async Task ArchiveSessionIgnoringErrorsAsync(
        string compatSessionId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dependencies.Api.ArchiveSessionAsync(compatSessionId, cancellationToken);
        }
        catch (Exception error)
        {
            _dependencies.Logger.LogVerbose(
                $"Failed to archive session {sessionId}: {error.Message}");
        }
    }
}
