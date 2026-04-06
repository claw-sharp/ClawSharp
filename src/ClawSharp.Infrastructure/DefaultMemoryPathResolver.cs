using ClawSharp.Core;
using System.Text;

namespace ClawSharp.Infrastructure;

public sealed class DefaultMemoryPathResolver : IMemoryPathResolver
{
    private readonly ClawSharpSettings _settings;
    private readonly string _memoryBaseDir;

    public DefaultMemoryPathResolver(ClawSharpSettings settings, string? memoryBaseDir = null)
    {
        _settings = settings;
        _memoryBaseDir = memoryBaseDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
    }

    public string GetMemoryDir(string workspaceRoot)
    {
        // 1. Check for env var override (CLAUDE_COWORK_MEMORY_PATH_OVERRIDE)
        var coworkOverride = Environment.GetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE");
        var validatedOverride = ValidateMemoryPath(coworkOverride, expandTilde: false);
        if (!string.IsNullOrWhiteSpace(validatedOverride))
        {
            return validatedOverride;
        }

        // 2. Check settings.json (policy/local/user)
        // Note: project-level .claude/settings.json is ignored for security in TS
        var validatedSetting = ValidateMemoryPath(_settings.Runtime.AutoMemoryDirectory, expandTilde: true);
        if (!string.IsNullOrWhiteSpace(validatedSetting))
        {
             return validatedSetting;
        }

        // 3. Fallback to <memoryBase>/projects/<sanitized-git-root>/memory/
        var projectsDir = Path.Combine(_memoryBaseDir, "projects");
        var gitRoot = ResolveCanonicalGitRoot(workspaceRoot);
        var sanitizedRoot = SessionStoragePaths.SanitizePath(gitRoot);

        return Path.Combine(projectsDir, sanitizedRoot, "memory");
    }

    public string GetMemoryEntrypoint(string workspaceRoot)
    {
        return Path.Combine(GetMemoryDir(workspaceRoot), "MEMORY.md");
    }

    private static string ResolveCanonicalGitRoot(string workspaceRoot)
    {
        // For now, using a simplified version: if it's a git repo, use the root.
        // Full worktree 'commondir' resolution can be added if needed for 1:1 parity with complex worktrees.
        // But for most cases, the workspaceRoot is the project root.
        return workspaceRoot; 
    }

    private static string? ValidateMemoryPath(string? raw, bool expandTilde)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Contains('\0'))
        {
            return null;
        }

        var candidate = raw;
        if (expandTilde &&
            (candidate.StartsWith("~/", StringComparison.Ordinal) ||
             candidate.StartsWith("~\\", StringComparison.Ordinal)))
        {
            var rest = candidate[2..];
            var normalizedRemainder = NormalizeRelativePath(rest);
            if (normalizedRemainder is "." or "..")
            {
                return null;
            }

            candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                rest);
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }

        if (!Path.IsPathRooted(normalized) ||
            normalized.Length < 3 ||
            IsDriveRoot(normalized) ||
            normalized.StartsWith(@"\\", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return (normalized + Path.DirectorySeparatorChar)
            .Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeRelativePath(string path)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var normalized = new Stack<string>();
        var aboveRoot = false;
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (normalized.Count > 0)
                {
                    normalized.Pop();
                }
                else
                {
                    aboveRoot = true;
                }
                continue;
            }

            normalized.Push(segment);
        }

        if (normalized.Count == 0)
        {
            return aboveRoot ? ".." : ".";
        }

        var orderedSegments = normalized.Reverse();
        return (aboveRoot ? ".." + Path.DirectorySeparatorChar : string.Empty) +
               string.Join(Path.DirectorySeparatorChar, orderedSegments);
    }

    private static bool IsDriveRoot(string path)
    {
        return path.Length == 2 &&
               char.IsLetter(path[0]) &&
               path[1] == ':';
    }
}
