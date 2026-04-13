using System.Collections.Concurrent;

namespace ClawSharp.Tools;

public sealed record CronFireRequest(
    string JobId,
    string Prompt,
    bool Recurring,
    bool Durable,
    string? SessionId,
    string? AgentId);

public sealed class CronSchedulerService : IAsyncDisposable
{
    public static readonly TimeSpan DefaultRecurringMaxAge = TimeSpan.FromDays(7);

    private readonly string _workspaceRoot;
    private readonly TimeSpan _tickInterval;
    private readonly TimeZoneInfo _timeZone;
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    private CancellationTokenSource? _loopCancellationSource;
    private Task? _loopTask;
    private Func<bool>? _isIdle;
    private Func<CronFireRequest, CancellationToken, Task<bool>>? _onFireAsync;

    public CronSchedulerService(
        string workspaceRoot,
        TimeSpan? tickInterval = null,
        TimeZoneInfo? timeZone = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(1);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public bool IsRunning => _loopTask is not null;

    public void Start(
        Func<bool> isIdle,
        Func<CronFireRequest, CancellationToken, Task<bool>> onFireAsync)
    {
        ArgumentNullException.ThrowIfNull(isIdle);
        ArgumentNullException.ThrowIfNull(onFireAsync);

        if (_loopTask is not null)
        {
            return;
        }

        _isIdle = isIdle;
        _onFireAsync = onFireAsync;
        _loopCancellationSource = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(_loopCancellationSource.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_loopCancellationSource is null || _loopTask is null)
        {
            return;
        }

        try
        {
            _loopCancellationSource.Cancel();
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loopCancellationSource.Dispose();
            _loopCancellationSource = null;
            _loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_tickInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await CheckAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (_isIdle is null || _onFireAsync is null || !_isIdle())
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var job in CronJobStore.ListAll(_workspaceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_inFlight.TryAdd(job.Id, 0))
            {
                continue;
            }

            try
            {
                if (!CronExpression.TryParse(job.Cron, out var expression) || expression is null)
                {
                    continue;
                }

                var anchorUtc = job.LastFiredAtUtc ?? job.CreatedAtUtc;
                var nextOccurrenceUtc = expression.GetNextOccurrence(anchorUtc, _timeZone);
                if (nextOccurrenceUtc is null || nowUtc < nextOccurrenceUtc.Value)
                {
                    continue;
                }

                if (!_isIdle())
                {
                    continue;
                }

                var fired = await _onFireAsync(
                    new CronFireRequest(job.Id, job.Prompt, job.Recurring, job.Durable, job.SessionId, job.AgentId),
                    cancellationToken).ConfigureAwait(false);
                if (!fired)
                {
                    continue;
                }

                var agedOut = job.Recurring && nowUtc - job.CreatedAtUtc >= DefaultRecurringMaxAge;
                if (job.Recurring && !agedOut)
                {
                    CronJobStore.MarkFired(_workspaceRoot, job.Id, nowUtc);
                }
                else
                {
                    CronJobStore.Remove(_workspaceRoot, job.Id);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best-effort scheduler. Leave the job in place and retry next tick.
            }
            finally
            {
                _inFlight.TryRemove(job.Id, out _);
            }
        }
    }
}
