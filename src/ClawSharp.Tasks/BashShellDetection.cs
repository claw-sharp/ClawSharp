// TS origin: ./utils/Shell.ts
using System.Diagnostics;

namespace ClawSharp.Tasks;

public static class BashShellDetection
{
    private const string MissingGitBashMessage = "Claude Code on Windows requires git-bash (https://git-scm.com/downloads/win). If installed but not in PATH, set environment variable pointing to your bash.exe, similar to: CLAUDE_CODE_GIT_BASH_PATH=C:\\Program Files\\Git\\bin\\bash.exe";
    private static readonly Lock CacheLock = new();
    private static Task<string?>? _cachedShellTask;
    private static readonly string[] WindowsGitDefaultLocations =
    [
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files (x86)\Git\cmd\git.exe"
    ];

    public static Task<string?> FindSuitableShellAsync()
    {
        lock (CacheLock)
        {
            _cachedShellTask ??= FindSuitableShellCoreAsync();
            return _cachedShellTask;
        }
    }

    public static void ResetCache()
    {
        lock (CacheLock)
        {
            _cachedShellTask = null;
        }
    }

    public static string FindGitBashPathWindowsOrThrow()
    {
        return FindGitBashPathWindowsOrThrow(
            Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH"),
            Environment.CurrentDirectory,
            static path => File.Exists(path),
            executable => FindExecutableOnWindows(
                executable,
                Environment.CurrentDirectory,
                static path => File.Exists(path),
                RunWhereExe));
    }

    private static Task<string?> FindSuitableShellCoreAsync()
    {
        var shellOverride = Environment.GetEnvironmentVariable("CLAUDE_CODE_SHELL");
        if (!string.IsNullOrWhiteSpace(shellOverride) &&
            IsSupportedShellType(shellOverride) &&
            File.Exists(shellOverride))
        {
            return Task.FromResult<string?>(shellOverride);
        }

        var envShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(envShell) &&
            IsSupportedShellType(envShell) &&
            File.Exists(envShell))
        {
            return Task.FromResult<string?>(envShell);
        }

        if (OperatingSystem.IsWindows())
        {
            var gitBashPath = TryFindGitBashPathWindows(
                Environment.CurrentDirectory,
                static path => File.Exists(path),
                executable => FindExecutableOnWindows(
                    executable,
                    Environment.CurrentDirectory,
                    static path => File.Exists(path),
                    RunWhereExe));
            if (!string.IsNullOrWhiteSpace(gitBashPath))
            {
                return Task.FromResult<string?>(gitBashPath);
            }
        }

        foreach (var candidate in GetCandidateShells())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Task.FromResult<string?>(candidate);
            }
        }

        return Task.FromResult<string?>(null);
    }

    private static IEnumerable<string> GetCandidateShells()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var shellName in OperatingSystem.IsWindows() ? new[] { "bash.exe", "zsh.exe" } : new[] { "bash", "zsh" })
                {
                    yield return Path.Combine(entry, shellName);
                }
            }
        }

        foreach (var fallback in OperatingSystem.IsWindows()
                     ? Array.Empty<string>()
                     : new[]
                     {
                         "/bin/bash",
                         "/usr/bin/bash",
                         "/usr/local/bin/bash",
                         "/bin/zsh",
                         "/usr/bin/zsh",
                         "/usr/local/bin/zsh",
                         "/opt/homebrew/bin/bash",
                         "/opt/homebrew/bin/zsh"
                     })
        {
            yield return fallback;
        }
    }

    private static bool IsSupportedShellType(string path)
    {
        return path.Contains("bash", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("zsh", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindGitBashPathWindowsOrThrow(
        string? configuredGitBashPath,
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> findExecutable)
    {
        if (!string.IsNullOrWhiteSpace(configuredGitBashPath))
        {
            if (pathExists(configuredGitBashPath))
            {
                return configuredGitBashPath;
            }

            throw new InvalidOperationException(
                $"Claude Code was unable to find CLAUDE_CODE_GIT_BASH_PATH path \"{configuredGitBashPath}\"");
        }

        var gitBashPath = TryFindGitBashPathWindows(currentWorkingDirectory, pathExists, findExecutable);
        return string.IsNullOrWhiteSpace(gitBashPath)
            ? throw new InvalidOperationException(MissingGitBashMessage)
            : gitBashPath;
    }

    private static string? TryFindGitBashPathWindows(
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> findExecutable)
    {
        var gitPath = findExecutable("git");
        if (string.IsNullOrWhiteSpace(gitPath))
        {
            return null;
        }

        var bashPath = Path.GetFullPath(Path.Combine(gitPath, "..", "..", "bin", "bash.exe"));
        return pathExists(bashPath)
            ? bashPath
            : null;
    }

    private static string? FindExecutableOnWindows(
        string executable,
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> runWhere)
    {
        if (string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var location in WindowsGitDefaultLocations)
            {
                if (pathExists(location))
                {
                    return location;
                }
            }
        }

        var result = runWhere(executable);
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        var normalizedCwd = Path.GetFullPath(currentWorkingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var candidatePath in result
                     .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalizedPath;
            string? pathDirectory;
            try
            {
                normalizedPath = Path.GetFullPath(candidatePath);
                pathDirectory = Path.GetDirectoryName(normalizedPath);
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pathDirectory) &&
                (string.Equals(pathDirectory, normalizedCwd, StringComparison.OrdinalIgnoreCase) ||
                 normalizedPath.StartsWith(normalizedCwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                 normalizedPath.StartsWith(normalizedCwd + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (pathExists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        return null;
    }

    private static string? RunWhereExe(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(executable);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0
                ? output.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
