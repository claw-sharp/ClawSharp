using System.Collections.Concurrent;
using System.Diagnostics;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Services;

public sealed class WorkspaceApplicationRegistry
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ClawSharpApplication>>> _applications =
        new(StringComparer.Ordinal);

    public async Task<ClawSharpApplication> GetOrCreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var cacheHit = _applications.ContainsKey(normalizedWorkspaceRoot);
        Log(
            cacheHit ? "reuse-request" : "create-request",
            $"workspace={normalizedWorkspaceRoot}");

        var lazy = _applications.GetOrAdd(
            normalizedWorkspaceRoot,
            static path => new Lazy<Task<ClawSharpApplication>>(
                () => ClawSharpApplicationFactory.CreateForWorkspaceAsync(path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var createdNewLazy = !cacheHit && ReferenceEquals(_applications[normalizedWorkspaceRoot], lazy);

        try
        {
            if (createdNewLazy)
            {
                Log("create-start", $"workspace={normalizedWorkspaceRoot}");
            }

            var stopwatch = Stopwatch.StartNew();
            var app = await lazy.Value.WaitAsync(cancellationToken);
            stopwatch.Stop();

            Log(
                createdNewLazy ? "create-complete" : "reuse-complete",
                $"workspace={normalizedWorkspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds}");

            return app;
        }
        catch (Exception ex)
        {
            _applications.TryRemove(normalizedWorkspaceRoot, out _);
            Log(
                "create-failed",
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

    private static void Log(string category, string message)
    {
        Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] [AgentHost:workspace-registry:{category}] {message}");
    }
}
