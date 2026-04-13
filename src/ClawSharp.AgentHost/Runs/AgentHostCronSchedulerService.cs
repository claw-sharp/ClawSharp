using System.Collections.Concurrent;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Tools;

namespace ClawSharp.AgentHost.Runs;

public sealed class AgentHostCronSchedulerService : IAsyncDisposable
{
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(5);

    private readonly RecentProjectStore _recentProjectStore;
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RunCoordinator _runCoordinator;
    private readonly ConcurrentDictionary<string, CronSchedulerService> _schedulers = new(StringComparer.Ordinal);

    private CancellationTokenSource? _loopCancellationSource;
    private Task? _loopTask;

    public AgentHostCronSchedulerService(
        RecentProjectStore recentProjectStore,
        WorkspaceApplicationRegistry applicationRegistry,
        RunCoordinator runCoordinator)
    {
        _recentProjectStore = recentProjectStore;
        _applicationRegistry = applicationRegistry;
        _runCoordinator = runCoordinator;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _loopCancellationSource = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(_loopCancellationSource.Token), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopCancellationSource is not null && _loopTask is not null)
        {
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

        foreach (var entry in _schedulers.ToArray())
        {
            if (_schedulers.TryRemove(entry.Key, out var scheduler))
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await EnsureSchedulersAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(DiscoveryInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await EnsureSchedulersAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureSchedulersAsync(CancellationToken cancellationToken)
    {
        var projects = await _recentProjectStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var activeWorkspaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workspaceRoot = WorkspaceApplicationRegistry.NormalizeWorkspaceRoot(project.Path);
            activeWorkspaces.Add(workspaceRoot);
            if (_schedulers.ContainsKey(workspaceRoot))
            {
                continue;
            }

            var durableStorePath = Path.Combine(workspaceRoot, ".clawsharp", "scheduled_tasks.json");
            if (!File.Exists(durableStorePath))
            {
                continue;
            }

            var scheduler = new CronSchedulerService(workspaceRoot);
            if (!_schedulers.TryAdd(workspaceRoot, scheduler))
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            scheduler.Start(
                isIdle: static () => true,
                onFireAsync: (job, token) => HandleFireAsync(workspaceRoot, job, token));
            AgentHostLog.Info("cron:scheduler-started", $"workspace={workspaceRoot}");
        }

        foreach (var existingWorkspace in _schedulers.Keys.ToArray())
        {
            if (activeWorkspaces.Contains(existingWorkspace))
            {
                continue;
            }

            if (_schedulers.TryRemove(existingWorkspace, out var scheduler))
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> HandleFireAsync(
        string workspaceRoot,
        CronFireRequest job,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = await ResolveProjectIdAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return false;
            }

            var sessionId = job.SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                var app = await _applicationRegistry.GetOrCreateAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
                sessionId = app.AppState.ActiveSessionId;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = (await app.SessionLogStore.LoadProjectLogsAsync(workspaceRoot, cancellationToken).ConfigureAwait(false))
                        .OrderByDescending(static log => log.Modified)
                        .Select(static log => log.SessionId)
                        .FirstOrDefault();
                }
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                AgentHostLog.Debug("cron:fire-skipped", $"workspace={workspaceRoot} jobId={job.JobId} reason=missing-session");
                return false;
            }

            return await _runCoordinator.TryStartScheduledRunAsync(projectId, sessionId, job.Prompt, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            AgentHostLog.Warn(
                "cron:fire-failed",
                $"workspace={workspaceRoot} jobId={job.JobId} error={error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private async Task<string?> ResolveProjectIdAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var projects = await _recentProjectStore.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var project in projects)
        {
            if (string.Equals(
                WorkspaceApplicationRegistry.NormalizeWorkspaceRoot(project.Path),
                workspaceRoot,
                StringComparison.Ordinal))
            {
                return project.ProjectId;
            }
        }

        return null;
    }
}
