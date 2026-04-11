using System.Collections.Concurrent;
using System.Diagnostics;
using ClawSharp.AgentHost;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Services;

public sealed class WorkspaceApplicationRegistry
{
    private static readonly TimeSpan StartupWaitLogInterval = TimeSpan.FromSeconds(5);
    private readonly ClawSharpApplicationFactoryOptions? _factoryOptions;
    private readonly Func<string, CancellationToken, ClawSharpApplicationFactoryOptions?, Task<ClawSharpApplication>> _applicationFactory;
    private readonly ConcurrentDictionary<string, Lazy<Task<ClawSharpApplication>>> _applications =
        new(GetPathComparer());

    public WorkspaceApplicationRegistry(
        ClawSharpApplicationFactoryOptions? factoryOptions = null,
        Func<string, CancellationToken, ClawSharpApplicationFactoryOptions?, Task<ClawSharpApplication>>? applicationFactory = null)
    {
        _factoryOptions = factoryOptions;
        _applicationFactory = applicationFactory ?? ClawSharpApplicationFactory.CreateForWorkspaceAsync;
    }

    public async Task<ClawSharpApplication> GetOrCreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var cacheHit = _applications.ContainsKey(normalizedWorkspaceRoot);
        AgentHostLog.Debug(
            cacheHit ? "workspace-registry:reuse-request" : "workspace-registry:create-request",
            $"workspace={normalizedWorkspaceRoot}");

        var lazy = _applications.GetOrAdd(
            normalizedWorkspaceRoot,
            path => new Lazy<Task<ClawSharpApplication>>(
                () => _applicationFactory(path, CancellationToken.None, _factoryOptions),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var createdNewLazy = !cacheHit && ReferenceEquals(_applications[normalizedWorkspaceRoot], lazy);

        try
        {
            if (createdNewLazy)
            {
                AgentHostLog.Debug("workspace-registry:create-start", $"workspace={normalizedWorkspaceRoot}");
            }

            var stopwatch = Stopwatch.StartNew();
            var app = await WaitForApplicationAsync(normalizedWorkspaceRoot, lazy, cancellationToken);
            stopwatch.Stop();

            AgentHostLog.Debug(
                createdNewLazy ? "workspace-registry:create-complete" : "workspace-registry:reuse-complete",
                $"workspace={normalizedWorkspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds}");

            return app;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AgentHostLog.Warn(
                "workspace-registry:wait-canceled",
                $"workspace={normalizedWorkspaceRoot}");
            throw;
        }
        catch (Exception ex)
        {
            _applications.TryRemove(normalizedWorkspaceRoot, out _);
            AgentHostLog.Warn(
                "workspace-registry:create-failed",
                $"workspace={normalizedWorkspaceRoot} error={ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new DirectoryNotFoundException("Project path is required.");
        }

        var normalized = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException($"Project directory '{normalized}' does not exist.");
        }

        return normalized;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static async Task<ClawSharpApplication> WaitForApplicationAsync(
        string workspaceRoot,
        Lazy<Task<ClawSharpApplication>> lazy,
        CancellationToken cancellationToken)
    {
        var task = lazy.Value;
        if (task.IsCompleted)
        {
            return await task.WaitAsync(cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var nextLogAt = StartupWaitLogInterval;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var delay = nextLogAt - stopwatch.Elapsed;
            if (delay <= TimeSpan.Zero)
            {
                delay = StartupWaitLogInterval;
                nextLogAt = stopwatch.Elapsed + StartupWaitLogInterval;
            }

            var completed = await Task.WhenAny(task, Task.Delay(delay, cancellationToken));
            if (completed == task)
            {
                return await task.WaitAsync(cancellationToken);
            }

            //AgentHostLog.Warn(
            //    "workspace-registry:wait-slow",
            //    $"workspace={workspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
    }
}
