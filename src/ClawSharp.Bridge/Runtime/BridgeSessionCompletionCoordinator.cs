namespace ClawSharp.Bridge;

public sealed record BridgeSessionCompletionState(
    IDictionary<string, ISessionHandle> ActiveSessions,
    IDictionary<string, DateTimeOffset> SessionStartTimes,
    IDictionary<string, string> SessionWorkIds,
    IDictionary<string, string> SessionIngressTokens,
    IDictionary<string, string> SessionCompatIds,
    IDictionary<string, BridgeSessionWorktree> SessionWorktrees,
    ISet<string> TimedOutSessions,
    ISet<string> TitledSessions,
    ISet<string> V2Sessions,
    ISet<Task> PendingCleanups);

public sealed record BridgeSessionCompletionDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    ICapacityWake CapacityWake,
    BridgeStopWorkRetryDependencies StopWorkRetryDependencies,
    Func<BridgeSessionWorktree, CancellationToken, Task>? RemoveAgentWorktreeAsync = null,
    Action? StopStatusUpdates = null,
    Action? StartStatusUpdates = null,
    Action<string>? CancelTokenRefresh = null,
    Action<string>? AbortLoop = null,
    Action<string>? OnDebug = null);

public sealed record BridgeSessionCompletionRequest(
    BridgeConfig Config,
    string EnvironmentId,
    string SessionId,
    DateTimeOffset StartTime,
    ISessionHandle Handle,
    SessionDoneStatus RawStatus,
    BridgeSessionCompletionState State,
    bool LoopAborted,
    double StopWorkBaseDelayMs = 1000d);

public sealed record BridgeSessionCompletionResult(
    SessionDoneStatus FinalStatus,
    string? WorkId,
    string CompatSessionId,
    bool StopWorkScheduled,
    bool WorktreeCleanupScheduled,
    bool ArchiveScheduled,
    bool RequestedLoopAbort);

public sealed class BridgeSessionCompletionCoordinator(BridgeSessionCompletionDependencies dependencies)
{
    public Task<BridgeSessionCompletionResult> CompleteAsync(
        BridgeSessionCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workId = request.State.SessionWorkIds.TryGetValue(request.SessionId, out var knownWorkId)
            ? knownWorkId
            : null;

        request.State.ActiveSessions.Remove(request.SessionId);
        request.State.SessionStartTimes.Remove(request.SessionId);
        request.State.SessionWorkIds.Remove(request.SessionId);
        request.State.SessionIngressTokens.Remove(request.SessionId);

        var compatSessionId = request.State.SessionCompatIds.TryGetValue(request.SessionId, out var knownCompatId)
            ? knownCompatId
            : request.SessionId;
        request.State.SessionCompatIds.Remove(request.SessionId);
        dependencies.Logger.RemoveSession(compatSessionId);
        request.State.TitledSessions.Remove(compatSessionId);
        request.State.V2Sessions.Remove(request.SessionId);
        dependencies.CancelTokenRefresh?.Invoke(request.SessionId);
        dependencies.CapacityWake.Wake();

        var wasTimedOut = request.State.TimedOutSessions.Remove(request.SessionId);
        var status = wasTimedOut && request.RawStatus == SessionDoneStatus.Interrupted
            ? SessionDoneStatus.Failed
            : request.RawStatus;
        var durationMs = (long)(DateTimeOffset.UtcNow - request.StartTime).TotalMilliseconds;

        dependencies.OnDebug?.Invoke(
            $"[bridge:session] sessionId={request.SessionId} workId={workId ?? "unknown"} exited status={status.ToString().ToLowerInvariant()} duration={BridgeRetryUtilities.FormatDelay(durationMs)}");

        dependencies.Logger.ClearStatus();
        dependencies.StopStatusUpdates?.Invoke();

        string? failureMessage = null;
        switch (status)
        {
            case SessionDoneStatus.Completed:
                dependencies.Logger.LogSessionComplete(request.SessionId, durationMs);
                break;

            case SessionDoneStatus.Failed:
                if (!wasTimedOut && !request.LoopAborted)
                {
                    failureMessage = request.Handle.LastStderr.Count > 0
                        ? string.Join('\n', request.Handle.LastStderr)
                        : "Process exited with error";
                    dependencies.Logger.LogSessionFailed(request.SessionId, failureMessage);
                }

                break;

            case SessionDoneStatus.Interrupted:
                dependencies.Logger.LogVerbose($"Session {request.SessionId} interrupted");
                break;
        }

        var stopWorkScheduled = false;
        if (status != SessionDoneStatus.Interrupted && workId is not null)
        {
            TrackCleanup(
                BridgeRetryUtilities.StopWorkWithRetryAsync(
                    dependencies.StopWorkRetryDependencies,
                    request.EnvironmentId,
                    workId,
                    request.StopWorkBaseDelayMs,
                    cancellationToken),
                request.State.PendingCleanups);
            stopWorkScheduled = true;
        }

        var worktreeCleanupScheduled = false;
        if (request.State.SessionWorktrees.TryGetValue(request.SessionId, out var worktree))
        {
            request.State.SessionWorktrees.Remove(request.SessionId);
            if (dependencies.RemoveAgentWorktreeAsync is not null)
            {
                TrackCleanup(
                    RemoveWorktreeIgnoringErrorsAsync(worktree, cancellationToken),
                    request.State.PendingCleanups);
                worktreeCleanupScheduled = true;
            }
        }

        var archiveScheduled = false;
        var requestedLoopAbort = false;
        if (status != SessionDoneStatus.Interrupted && !request.LoopAborted)
        {
            if (request.Config.SpawnMode != SpawnMode.SingleSession)
            {
                TrackCleanup(
                    ArchiveSessionIgnoringErrorsAsync(compatSessionId, request.SessionId, cancellationToken),
                    request.State.PendingCleanups);
                archiveScheduled = true;
                dependencies.OnDebug?.Invoke(
                    $"[bridge:session] Session {status.ToString().ToLowerInvariant()}, returning to idle (multi-session mode)");
            }
            else
            {
                dependencies.OnDebug?.Invoke(
                    $"[bridge:session] Session {status.ToString().ToLowerInvariant()}, aborting poll loop to tear down environment");
                dependencies.AbortLoop?.Invoke(request.SessionId);
                requestedLoopAbort = true;
                return Task.FromResult(new BridgeSessionCompletionResult(
                    status,
                    workId,
                    compatSessionId,
                    stopWorkScheduled,
                    worktreeCleanupScheduled,
                    archiveScheduled,
                    requestedLoopAbort));
            }
        }

        if (!request.LoopAborted)
        {
            dependencies.StartStatusUpdates?.Invoke();
        }

        return Task.FromResult(new BridgeSessionCompletionResult(
            status,
            workId,
            compatSessionId,
            stopWorkScheduled,
            worktreeCleanupScheduled,
            archiveScheduled,
            requestedLoopAbort));
    }

    private async Task RemoveWorktreeIgnoringErrorsAsync(
        BridgeSessionWorktree worktree,
        CancellationToken cancellationToken)
    {
        try
        {
            await dependencies.RemoveAgentWorktreeAsync!(worktree, cancellationToken);
        }
        catch (Exception error)
        {
            dependencies.Logger.LogVerbose($"Failed to remove worktree {worktree.WorktreePath}: {error.Message}");
        }
    }

    private async Task ArchiveSessionIgnoringErrorsAsync(
        string compatSessionId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await dependencies.Api.ArchiveSessionAsync(compatSessionId, cancellationToken);
        }
        catch (Exception error)
        {
            dependencies.Logger.LogVerbose($"Failed to archive session {sessionId}: {error.Message}");
        }
    }

    private static void TrackCleanup(Task task, ISet<Task> pendingCleanups)
    {
        lock (pendingCleanups)
        {
            pendingCleanups.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (pendingCleanups)
                {
                    pendingCleanups.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
