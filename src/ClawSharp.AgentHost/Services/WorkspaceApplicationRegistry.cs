using System.Collections.Concurrent;
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
        var lazy = _applications.GetOrAdd(
            normalizedWorkspaceRoot,
            static path => new Lazy<Task<ClawSharpApplication>>(
                () => ClawSharpApplicationFactory.CreateForWorkspaceAsync(path),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _applications.TryRemove(normalizedWorkspaceRoot, out _);
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
}
