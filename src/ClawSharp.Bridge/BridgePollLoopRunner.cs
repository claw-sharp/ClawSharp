namespace ClawSharp.Bridge;

public sealed record BridgeBackoffConfig(
    double ConnInitialMs,
    double ConnCapMs,
    double ConnGiveUpMs,
    double GeneralInitialMs,
    double GeneralCapMs,
    double GeneralGiveUpMs,
    double? ShutdownGraceMs = null,
    double? StopWorkBaseDelayMs = null)
{
    public static BridgeBackoffConfig Default { get; } = new(
        ConnInitialMs: 2_000,
        ConnCapMs: 120_000,
        ConnGiveUpMs: 600_000,
        GeneralInitialMs: 500,
        GeneralCapMs: 30_000,
        GeneralGiveUpMs: 600_000);
}

public enum BridgeHeartbeatResult
{
    Ok,
    AuthFailed,
    Fatal,
    Failed
}

public enum BridgeHeartbeatModeExitReason
{
    AuthFailed,
    Fatal,
    Shutdown,
    CapacityChanged,
    PollDue,
    ConfigDisabled
}

public sealed record BridgePollLoopHeartbeatModeResult(
    BridgeHeartbeatResult Result,
    int HeartbeatCycles,
    BridgeHeartbeatModeExitReason ExitReason);

public sealed record BridgePollLoopResult(
    bool FatalExit,
    BridgeFatalError? FatalError = null);

public sealed record BridgePollLoopDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    ICapacityWake CapacityWake,
    Func<BridgePollConfig> GetPollConfig,
    Func<int> GetActiveSessionCount,
    Func<BridgeWorkResponse, bool, BridgePollConfig, CancellationToken, Task> OnWorkAsync,
    Func<CancellationToken, Task<BridgeHeartbeatResult>> HeartbeatActiveWorkItemsAsync,
    Func<double, CancellationToken, Task>? SleepAsync = null,
    Func<DateTimeOffset>? GetNow = null,
    Action<string>? OnDebug = null,
    Action<BridgePollLoopHeartbeatModeResult>? OnHeartbeatModeExited = null,
    Func<Exception, bool>? IsConnectionError = null,
    Func<Exception, bool>? IsServerError = null,
    Func<Exception, string>? DescribeError = null);

public static class BridgePollLoopRunner
{
    public static async Task<BridgePollLoopResult> RunAsync(
        BridgePollLoopDependencies dependencies,
        string environmentId,
        string environmentSecret,
        int maxSessions,
        CancellationToken cancellationToken = default,
        BridgeBackoffConfig? backoffConfig = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(environmentId);
        ArgumentNullException.ThrowIfNull(environmentSecret);

        backoffConfig ??= BridgeBackoffConfig.Default;
        var sleepAsync = dependencies.SleepAsync ?? DefaultSleepAsync;
        var getNow = dependencies.GetNow ?? (() => DateTimeOffset.UtcNow);
        var isConnectionError = dependencies.IsConnectionError ?? BridgeMainArgumentUtilities.IsConnectionError;
        var isServerError = dependencies.IsServerError ?? BridgeMainArgumentUtilities.IsServerError;
        var describeError = dependencies.DescribeError ?? ((Exception error) => error.Message);

        double connBackoff = 0;
        double generalBackoff = 0;
        DateTimeOffset? connErrorStart = null;
        DateTimeOffset? generalErrorStart = null;
        DateTimeOffset? lastPollErrorTime = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var pollConfig = dependencies.GetPollConfig();

            try
            {
                var work = await dependencies.Api.PollForWorkAsync(
                    environmentId,
                    environmentSecret,
                    cancellationToken,
                    pollConfig.ReclaimOlderThanMs);

                connBackoff = 0;
                generalBackoff = 0;
                connErrorStart = null;
                generalErrorStart = null;
                lastPollErrorTime = null;

                if (work is null)
                {
                    await HandleNoWorkAsync(
                        dependencies,
                        environmentId,
                        environmentSecret,
                        maxSessions,
                        pollConfig,
                        sleepAsync,
                        cancellationToken);
                    continue;
                }

                var atCapacityBeforeSwitch = dependencies.GetActiveSessionCount() >= maxSessions;
                await dependencies.OnWorkAsync(work, atCapacityBeforeSwitch, pollConfig, cancellationToken);

                if (atCapacityBeforeSwitch)
                {
                    await SleepAtCapacityAsync(dependencies, pollConfig, sleepAsync, cancellationToken);
                }
            }
            catch (Exception error)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (error is BridgeFatalError fatalError)
                {
                    if (BridgeApiErrorUtilities.IsExpiredErrorType(fatalError.ErrorType))
                    {
                        dependencies.Logger.LogStatus(fatalError.Message);
                    }
                    else if (BridgeApiErrorUtilities.IsSuppressible403(fatalError))
                    {
                        dependencies.OnDebug?.Invoke($"[bridge:work] Suppressed 403 error: {fatalError.Message}");
                    }
                    else
                    {
                        dependencies.Logger.LogError(fatalError.Message);
                    }

                    return new BridgePollLoopResult(true, fatalError);
                }

                var now = getNow();
                if (isConnectionError(error) || isServerError(error))
                {
                    if (ShouldResetForSleep(lastPollErrorTime, now, backoffConfig))
                    {
                        dependencies.OnDebug?.Invoke(
                            $"[bridge:work] Detected system sleep ({Math.Round((now - lastPollErrorTime!.Value).TotalSeconds)}s gap), resetting error budget");
                        connErrorStart = null;
                        connBackoff = 0;
                        generalErrorStart = null;
                        generalBackoff = 0;
                    }

                    lastPollErrorTime = now;
                    connErrorStart ??= now;

                    var elapsed = (now - connErrorStart.Value).TotalMilliseconds;
                    if (elapsed >= backoffConfig.ConnGiveUpMs)
                    {
                        dependencies.Logger.LogError(
                            $"Server unreachable for {Math.Round(elapsed / 60_000)} minutes, giving up.");
                        return new BridgePollLoopResult(true);
                    }

                    generalErrorStart = null;
                    generalBackoff = 0;

                    connBackoff = connBackoff > 0
                        ? Math.Min(connBackoff * 2, backoffConfig.ConnCapMs)
                        : backoffConfig.ConnInitialMs;
                    var delay = BridgeRetryUtilities.AddJitter(connBackoff, Random.Shared.NextDouble());
                    dependencies.Logger.LogVerbose(
                        $"Connection error, retrying in {BridgeRetryUtilities.FormatDelay(delay)} ({Math.Round(elapsed / 1000)}s elapsed): {describeError(error)}");
                    dependencies.Logger.UpdateReconnectingStatus(
                        BridgeRetryUtilities.FormatDelay(delay),
                        FormatDuration(elapsed));

                    if (dependencies.GetPollConfig().NonExclusiveHeartbeatIntervalMs > 0)
                    {
                        await dependencies.HeartbeatActiveWorkItemsAsync(cancellationToken);
                    }

                    await sleepAsync(delay, cancellationToken);
                    continue;
                }

                if (ShouldResetForSleep(lastPollErrorTime, now, backoffConfig))
                {
                    dependencies.OnDebug?.Invoke(
                        $"[bridge:work] Detected system sleep ({Math.Round((now - lastPollErrorTime!.Value).TotalSeconds)}s gap), resetting error budget");
                    connErrorStart = null;
                    connBackoff = 0;
                    generalErrorStart = null;
                    generalBackoff = 0;
                }

                lastPollErrorTime = now;
                generalErrorStart ??= now;

                var generalElapsed = (now - generalErrorStart.Value).TotalMilliseconds;
                if (generalElapsed >= backoffConfig.GeneralGiveUpMs)
                {
                    dependencies.Logger.LogError(
                        $"Persistent errors for {Math.Round(generalElapsed / 60_000)} minutes, giving up.");
                    return new BridgePollLoopResult(true);
                }

                connErrorStart = null;
                connBackoff = 0;

                generalBackoff = generalBackoff > 0
                    ? Math.Min(generalBackoff * 2, backoffConfig.GeneralCapMs)
                    : backoffConfig.GeneralInitialMs;
                var generalDelay = BridgeRetryUtilities.AddJitter(generalBackoff, Random.Shared.NextDouble());
                dependencies.Logger.LogVerbose(
                    $"Poll failed, retrying in {BridgeRetryUtilities.FormatDelay(generalDelay)} ({Math.Round(generalElapsed / 1000)}s elapsed): {describeError(error)}");
                dependencies.Logger.UpdateReconnectingStatus(
                    BridgeRetryUtilities.FormatDelay(generalDelay),
                    FormatDuration(generalElapsed));

                if (dependencies.GetPollConfig().NonExclusiveHeartbeatIntervalMs > 0)
                {
                    await dependencies.HeartbeatActiveWorkItemsAsync(cancellationToken);
                }

                await sleepAsync(generalDelay, cancellationToken);
            }
        }

        return new BridgePollLoopResult(false);
    }

    private static async Task HandleNoWorkAsync(
        BridgePollLoopDependencies dependencies,
        string environmentId,
        string environmentSecret,
        int maxSessions,
        BridgePollConfig pollConfig,
        Func<double, CancellationToken, Task> sleepAsync,
        CancellationToken cancellationToken)
    {
        var activeSessionCount = dependencies.GetActiveSessionCount();
        var atCapacity = activeSessionCount >= maxSessions;
        if (atCapacity)
        {
            var atCapMs = pollConfig.MultisessionPollIntervalMsAtCapacity;
            if (pollConfig.NonExclusiveHeartbeatIntervalMs > 0)
            {
                var result = await RunHeartbeatModeUntilPollDueAsync(
                    dependencies,
                    maxSessions,
                    atCapMs,
                    sleepAsync,
                    cancellationToken);
                dependencies.OnHeartbeatModeExited?.Invoke(result);

                if (result.Result is BridgeHeartbeatResult.AuthFailed or BridgeHeartbeatResult.Fatal)
                {
                    var signal = dependencies.CapacityWake.CreateSignal();
                    try
                    {
                        await sleepAsync(
                            atCapMs > 0 ? atCapMs : pollConfig.NonExclusiveHeartbeatIntervalMs,
                            signal.Token);
                    }
                    finally
                    {
                        signal.Cleanup();
                    }
                }
            }
            else if (atCapMs > 0)
            {
                var signal = dependencies.CapacityWake.CreateSignal();
                try
                {
                    await sleepAsync(atCapMs, signal.Token);
                }
                finally
                {
                    signal.Cleanup();
                }
            }

            return;
        }

        var interval = activeSessionCount > 0
            ? pollConfig.MultisessionPollIntervalMsPartialCapacity
            : pollConfig.MultisessionPollIntervalMsNotAtCapacity;
        await sleepAsync(interval, cancellationToken);
    }

    private static async Task<BridgePollLoopHeartbeatModeResult> RunHeartbeatModeUntilPollDueAsync(
        BridgePollLoopDependencies dependencies,
        int maxSessions,
        int atCapacityPollIntervalMs,
        Func<double, CancellationToken, Task> sleepAsync,
        CancellationToken cancellationToken)
    {
        var getNow = dependencies.GetNow ?? (() => DateTimeOffset.UtcNow);
        var pollDeadline = atCapacityPollIntervalMs > 0
            ? getNow().AddMilliseconds(atCapacityPollIntervalMs)
            : (DateTimeOffset?)null;
        var heartbeatResult = BridgeHeartbeatResult.Ok;
        var heartbeatCycles = 0;

        while (!cancellationToken.IsCancellationRequested &&
               dependencies.GetActiveSessionCount() >= maxSessions &&
               (pollDeadline is null || getNow() < pollDeadline.Value))
        {
            var heartbeatConfig = dependencies.GetPollConfig();
            if (heartbeatConfig.NonExclusiveHeartbeatIntervalMs <= 0)
            {
                return new BridgePollLoopHeartbeatModeResult(
                    heartbeatResult,
                    heartbeatCycles,
                    BridgeHeartbeatModeExitReason.ConfigDisabled);
            }

            heartbeatResult = await dependencies.HeartbeatActiveWorkItemsAsync(cancellationToken);
            if (heartbeatResult is BridgeHeartbeatResult.AuthFailed or BridgeHeartbeatResult.Fatal)
            {
                return new BridgePollLoopHeartbeatModeResult(
                    heartbeatResult,
                    heartbeatCycles,
                    heartbeatResult == BridgeHeartbeatResult.AuthFailed
                        ? BridgeHeartbeatModeExitReason.AuthFailed
                        : BridgeHeartbeatModeExitReason.Fatal);
            }

            heartbeatCycles++;
            var signal = dependencies.CapacityWake.CreateSignal();
            try
            {
                await sleepAsync(heartbeatConfig.NonExclusiveHeartbeatIntervalMs, signal.Token);
            }
            finally
            {
                signal.Cleanup();
            }
        }

        return new BridgePollLoopHeartbeatModeResult(
            heartbeatResult,
            heartbeatCycles,
            cancellationToken.IsCancellationRequested
                ? BridgeHeartbeatModeExitReason.Shutdown
                : dependencies.GetActiveSessionCount() < maxSessions
                    ? BridgeHeartbeatModeExitReason.CapacityChanged
                    : pollDeadline is not null && getNow() >= pollDeadline.Value
                        ? BridgeHeartbeatModeExitReason.PollDue
                        : BridgeHeartbeatModeExitReason.ConfigDisabled);
    }

    private static async Task SleepAtCapacityAsync(
        BridgePollLoopDependencies dependencies,
        BridgePollConfig pollConfig,
        Func<double, CancellationToken, Task> sleepAsync,
        CancellationToken cancellationToken)
    {
        var signal = dependencies.CapacityWake.CreateSignal();
        try
        {
            if (pollConfig.NonExclusiveHeartbeatIntervalMs > 0)
            {
                await dependencies.HeartbeatActiveWorkItemsAsync(cancellationToken);
                await sleepAsync(pollConfig.NonExclusiveHeartbeatIntervalMs, signal.Token);
            }
            else if (pollConfig.MultisessionPollIntervalMsAtCapacity > 0)
            {
                await sleepAsync(pollConfig.MultisessionPollIntervalMsAtCapacity, signal.Token);
            }
        }
        finally
        {
            signal.Cleanup();
        }
    }

    private static bool ShouldResetForSleep(
        DateTimeOffset? lastPollErrorTime,
        DateTimeOffset now,
        BridgeBackoffConfig backoffConfig)
    {
        return lastPollErrorTime is not null &&
               (now - lastPollErrorTime.Value).TotalMilliseconds > PollSleepDetectionThresholdMs(backoffConfig);
    }

    private static double PollSleepDetectionThresholdMs(BridgeBackoffConfig backoffConfig)
    {
        return backoffConfig.ConnCapMs * 2;
    }

    private static string FormatDuration(double elapsedMs)
    {
        if (elapsedMs < 1000)
        {
            return $"{Math.Round(elapsedMs)}ms";
        }

        if (elapsedMs < 60_000)
        {
            return $"{Math.Round(elapsedMs / 1000)}s";
        }

        return $"{Math.Round(elapsedMs / 60_000)}m";
    }

    private static async Task DefaultSleepAsync(double delayMs, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }
}
