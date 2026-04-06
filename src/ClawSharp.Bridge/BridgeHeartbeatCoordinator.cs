// TS origin: ./bridge/bridgeMain.ts, ./bridge/replBridge.ts
namespace ClawSharp.Bridge;

public sealed record BridgeHeartbeatWorkItem(
    string SessionId,
    string WorkId,
    string SessionToken);

public sealed record BridgeHeartbeatInfo(
    string EnvironmentId,
    string WorkId,
    string SessionToken);

public sealed record BridgeHeartbeatDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    Action<string>? OnDebug = null);

public static class BridgeHeartbeatCoordinator
{
    public static async Task<BridgeHeartbeatResult> HeartbeatActiveWorkItemsAsync(
        BridgeHeartbeatDependencies dependencies,
        string environmentId,
        IEnumerable<BridgeHeartbeatWorkItem> activeWorkItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(environmentId);
        ArgumentNullException.ThrowIfNull(activeWorkItems);

        var anySuccess = false;
        var anyFatal = false;
        var authFailedSessions = new List<string>();

        foreach (var workItem in activeWorkItems)
        {
            try
            {
                await dependencies.Api.HeartbeatWorkAsync(
                    environmentId,
                    workItem.WorkId,
                    workItem.SessionToken,
                    cancellationToken);
                anySuccess = true;
            }
            catch (Exception error)
            {
                dependencies.OnDebug?.Invoke(
                    $"[bridge:heartbeat] Failed for sessionId={workItem.SessionId} workId={workItem.WorkId}: {error.Message}");

                if (error is BridgeFatalError fatalError)
                {
                    if (fatalError.Status is 401 or 403)
                    {
                        authFailedSessions.Add(workItem.SessionId);
                    }
                    else
                    {
                        anyFatal = true;
                    }
                }
            }
        }

        foreach (var sessionId in authFailedSessions)
        {
            dependencies.Logger.LogVerbose(
                $"Session {sessionId} token expired — re-queuing via bridge/reconnect");

            try
            {
                await dependencies.Api.ReconnectSessionAsync(
                    environmentId,
                    sessionId,
                    cancellationToken);
                dependencies.OnDebug?.Invoke(
                    $"[bridge:heartbeat] Re-queued sessionId={sessionId} via bridge/reconnect");
            }
            catch (Exception error)
            {
                dependencies.Logger.LogError(
                    $"Failed to refresh session {sessionId} token: {error.Message}");
                dependencies.OnDebug?.Invoke(
                    $"[bridge:heartbeat] reconnectSession({sessionId}) failed: {error.Message}");
            }
        }

        if (anyFatal)
        {
            return BridgeHeartbeatResult.Fatal;
        }

        if (authFailedSessions.Count > 0)
        {
            return BridgeHeartbeatResult.AuthFailed;
        }

        return anySuccess ? BridgeHeartbeatResult.Ok : BridgeHeartbeatResult.Failed;
    }

    public static async Task TryHeartbeatCurrentWorkItemAsync(
        BridgeHeartbeatDependencies dependencies,
        BridgeHeartbeatInfo? heartbeatInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        if (heartbeatInfo is null)
        {
            return;
        }

        try
        {
            await dependencies.Api.HeartbeatWorkAsync(
                heartbeatInfo.EnvironmentId,
                heartbeatInfo.WorkId,
                heartbeatInfo.SessionToken,
                cancellationToken);
        }
        catch
        {
            // Best-effort — TS ignores heartbeat failures in the poll backoff path.
        }
    }
}
