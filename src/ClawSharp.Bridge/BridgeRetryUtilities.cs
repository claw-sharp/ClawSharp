using System.Globalization;

namespace ClawSharp.Bridge;

public sealed record BridgeStopWorkRetryDependencies(
    IBridgeApiClient Api,
    IBridgeLogger Logger,
    Action<string>? OnDebug = null,
    Action<int, bool?>? OnStopWorkFailed = null,
    Func<double, Task>? SleepAsync = null,
    Func<double>? NextRandomDouble = null);

public static class BridgeRetryUtilities
{
    public static double AddJitter(double milliseconds, double randomUnitInterval)
    {
        return Math.Max(0d, milliseconds + milliseconds * 0.25d * (2d * randomUnitInterval - 1d));
    }

    public static string FormatDelay(double milliseconds)
    {
        return milliseconds >= 1000d
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{milliseconds / 1000d:0.0}s")
            : $"{Math.Round(milliseconds)}ms";
    }

    public static async Task StopWorkWithRetryAsync(
        BridgeStopWorkRetryDependencies dependencies,
        string environmentId,
        string workId,
        double baseDelayMs = 1000d,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(environmentId);
        ArgumentNullException.ThrowIfNull(workId);

        const int maxAttempts = 3;
        var sleepAsync = dependencies.SleepAsync ?? (delay => Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken));
        var nextRandomDouble = dependencies.NextRandomDouble ?? Random.Shared.NextDouble;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dependencies.Api.StopWorkAsync(environmentId, workId, false, cancellationToken);
                dependencies.OnDebug?.Invoke(
                    $"[bridge:work] stopWork succeeded for workId={workId} on attempt {attempt}/{maxAttempts}");
                return;
            }
            catch (Exception error)
            {
                if (error is BridgeFatalError fatalError)
                {
                    if (BridgeApiErrorUtilities.IsSuppressible403(fatalError))
                    {
                        dependencies.OnDebug?.Invoke(
                            $"[bridge:work] Suppressed stopWork 403 for {workId}: {fatalError.Message}");
                    }
                    else
                    {
                        dependencies.Logger.LogError($"Failed to stop work {workId}: {fatalError.Message}");
                    }

                    dependencies.OnStopWorkFailed?.Invoke(attempt, true);
                    return;
                }

                var errorMessage = error.Message;
                if (attempt < maxAttempts)
                {
                    var delay = AddJitter(baseDelayMs * Math.Pow(2d, attempt - 1), nextRandomDouble());
                    dependencies.Logger.LogVerbose(
                        $"Failed to stop work {workId} (attempt {attempt}/{maxAttempts}), retrying in {FormatDelay(delay)}: {errorMessage}");
                    await sleepAsync(delay);
                }
                else
                {
                    dependencies.Logger.LogError(
                        $"Failed to stop work {workId} after {maxAttempts} attempts: {errorMessage}");
                    dependencies.OnStopWorkFailed?.Invoke(maxAttempts, null);
                }
            }
        }
    }
}
