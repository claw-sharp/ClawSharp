using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ClawSharp.Infrastructure;

public sealed record WorkspaceSearchPaths(
    string WorkspaceRoot,
    string? GitRoot,
    IReadOnlyList<string> AncestorDirectories)
{
    public IReadOnlyList<string> GetProjectConfigDirectories(string relativeLeafDirectory)
    {
        return AncestorDirectories
            .Select(path => Path.Combine(path, ".clawsharp", relativeLeafDirectory))
            .ToArray();
    }
}

public static class WorkspaceSearchPathResolver
{
    private static readonly TimeSpan GitCommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, Lazy<Task<WorkspaceSearchPaths>>> Cache =
        new(GetPathComparer());

    public static async Task<WorkspaceSearchPaths> ResolveAsync(
        string workspaceRoot,
        Func<string, CancellationToken, Task<string?>>? canonicalGitRootResolver = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var lazy = Cache.GetOrAdd(
            normalizedWorkspaceRoot,
            path => new Lazy<Task<WorkspaceSearchPaths>>(
                () => BuildAsync(path, canonicalGitRootResolver, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                Cache.TryRemove(normalizedWorkspaceRoot, out _);
            }

            throw;
        }
    }

    public static async Task<string?> TryGetCanonicalGitRootAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --show-toplevel",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GitCommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            await AwaitExitQuietlyAsync(process).ConfigureAwait(false);
            _ = await standardOutputTask.ConfigureAwait(false);
            _ = await standardErrorTask.ConfigureAwait(false);
            return null;
        }

        var output = (await standardOutputTask.ConfigureAwait(false)).Trim();
        _ = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
    }

    private static async Task<WorkspaceSearchPaths> BuildAsync(
        string workspaceRoot,
        Func<string, CancellationToken, Task<string?>>? canonicalGitRootResolver,
        CancellationToken cancellationToken)
    {
        var homeDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var gitRootResolver = canonicalGitRootResolver ?? TryGetCanonicalGitRootAsync;
        var gitRoot = await gitRootResolver(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var current = Path.GetFullPath(workspaceRoot);
        var directories = new List<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            directories.Add(current);

            if (PathsEqual(current, homeDirectory))
            {
                break;
            }

            if (gitRoot is not null && PathsEqual(current, gitRoot))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
        }

        return new WorkspaceSearchPaths(workspaceRoot, gitRoot, directories);
    }

    private static async Task AwaitExitQuietlyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
