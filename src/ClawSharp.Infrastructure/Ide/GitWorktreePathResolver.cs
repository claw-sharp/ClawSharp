using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ClawSharp.Infrastructure;

public sealed class GitWorktreePathResolver : IWorktreePathResolver
{
    public async Task<IReadOnlyList<string>> GetWorktreePathsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "worktree list --porcelain",
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
                return [];
            }
        }
        catch
        {
            return [];
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            return [];
        }

        var worktreePaths = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(line => line["worktree ".Length..].Normalize(NormalizationForm.FormC))
            .Distinct(GetPathComparer())
            .ToList();

        if (worktreePaths.Count == 0)
        {
            return [];
        }

        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot).Normalize(NormalizationForm.FormC);
        string? currentWorktree = null;
        foreach (var path in worktreePaths)
        {
            if (PathEqualsOrContains(normalizedWorkspaceRoot, path))
            {
                currentWorktree = path;
                break;
            }
        }

        var otherWorktrees = worktreePaths
            .Where(path => !PathEquals(path, currentWorktree))
            .OrderBy(path => path, GetPathComparer())
            .ToList();

        return currentWorktree is null
            ? otherWorktrees
            : [currentWorktree, .. otherWorktrees];
    }

    private static bool PathEqualsOrContains(string candidate, string root)
    {
        var normalizedCandidate = Path.GetFullPath(candidate).Normalize(NormalizationForm.FormC);
        var normalizedRoot = Path.GetFullPath(root).Normalize(NormalizationForm.FormC);
        if (PathEquals(normalizedCandidate, normalizedRoot))
        {
            return true;
        }

        var separator = Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture);
        return normalizedCandidate.StartsWith(normalizedRoot + separator, GetComparison());
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left).Normalize(NormalizationForm.FormC),
            Path.GetFullPath(right).Normalize(NormalizationForm.FormC),
            GetComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
