// TS origin: ./utils/Shell.ts, ./utils/windowsPaths.ts
using System.Reflection;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

public sealed class BashShellDetectionTests
{
    [Fact]
    public async Task FindSuitableShellAsync_Prefers_ClaudeCodeShell_Override_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-bash-detection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var shellOverride = Path.Combine(tempRoot, "override-bash.exe");
        var envShell = Path.Combine(tempRoot, "env-zsh.exe");
        await File.WriteAllTextAsync(shellOverride, string.Empty);
        await File.WriteAllTextAsync(envShell, string.Empty);

        using var shellOverrideScope = new EnvironmentVariableScope("CLAUDE_CODE_SHELL", shellOverride);
        using var envShellScope = new EnvironmentVariableScope("SHELL", envShell);

        BashShellDetection.ResetCache();

        try
        {
            var shell = await BashShellDetection.FindSuitableShellAsync();

            Assert.Equal(shellOverride, shell);
        }
        finally
        {
            BashShellDetection.ResetCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FindSuitableShellAsync_Falls_Back_To_Shell_Environment_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-bash-detection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var envShell = Path.Combine(tempRoot, "env-bash.exe");
        await File.WriteAllTextAsync(envShell, string.Empty);

        using var shellOverrideScope = new EnvironmentVariableScope("CLAUDE_CODE_SHELL", Path.Combine(tempRoot, "missing-bash.exe"));
        using var envShellScope = new EnvironmentVariableScope("SHELL", envShell);

        BashShellDetection.ResetCache();

        try
        {
            var shell = await BashShellDetection.FindSuitableShellAsync();

            Assert.Equal(envShell, shell);
        }
        finally
        {
            BashShellDetection.ResetCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FindSuitableShellAsync_Prefers_ClaudeCodeShell_Override_On_Linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-bash-detection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var shellOverride = Path.Combine(tempRoot, "override-bash");
        var envShell = Path.Combine(tempRoot, "env-zsh");
        await File.WriteAllTextAsync(shellOverride, string.Empty);
        await File.WriteAllTextAsync(envShell, string.Empty);

        using var shellOverrideScope = new EnvironmentVariableScope("CLAUDE_CODE_SHELL", shellOverride);
        using var envShellScope = new EnvironmentVariableScope("SHELL", envShell);

        BashShellDetection.ResetCache();

        try
        {
            var shell = await BashShellDetection.FindSuitableShellAsync();

            Assert.Equal(shellOverride, shell);
        }
        finally
        {
            BashShellDetection.ResetCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FindSuitableShellAsync_Falls_Back_To_Shell_Environment_On_Linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-bash-detection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var envShell = Path.Combine(tempRoot, "env-bash");
        await File.WriteAllTextAsync(envShell, string.Empty);

        using var shellOverrideScope = new EnvironmentVariableScope("CLAUDE_CODE_SHELL", Path.Combine(tempRoot, "missing-bash"));
        using var envShellScope = new EnvironmentVariableScope("SHELL", envShell);

        BashShellDetection.ResetCache();

        try
        {
            var shell = await BashShellDetection.FindSuitableShellAsync();

            Assert.Equal(envShell, shell);
        }
        finally
        {
            BashShellDetection.ResetCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void FindExecutableOnWindows_Prefers_Default_Git_Locations()
    {
        var result = FindExecutableOnWindows(
            "git",
            @"C:\repo",
            path => string.Equals(path, @"C:\Program Files\Git\cmd\git.exe", StringComparison.OrdinalIgnoreCase),
            _ => null);

        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", result);
    }

    [Fact]
    public void FindExecutableOnWindows_Skips_Current_Directory_Candidates()
    {
        var result = FindExecutableOnWindows(
            "git",
            @"C:\repo",
            path => string.Equals(path, Path.GetFullPath(@"C:\tools\git.exe"), StringComparison.OrdinalIgnoreCase),
            _ => string.Join(
                "\r\n",
                [
                    @"C:\repo\git.exe",
                    @"C:\tools\git.exe"
                ]));

        Assert.Equal(Path.GetFullPath(@"C:\tools\git.exe"), result);
    }

    [Fact]
    public void TryFindGitBashPathWindows_Uses_Git_Cmd_Path_To_Resolve_Bash()
    {
        var result = TryFindGitBashPathWindows(
            @"C:\repo",
            path => string.Equals(path, Path.GetFullPath(@"C:\Program Files\Git\bin\bash.exe"), StringComparison.OrdinalIgnoreCase),
            _ => @"C:\Program Files\Git\cmd\git.exe");

        Assert.Equal(Path.GetFullPath(@"C:\Program Files\Git\bin\bash.exe"), result);
    }

    [Fact]
    public void FindGitBashPathWindowsOrThrow_Uses_Configured_Path_When_Present()
    {
        var result = FindGitBashPathWindowsOrThrow(
            @"C:\custom\bash.exe",
            @"C:\repo",
            path => string.Equals(path, @"C:\custom\bash.exe", StringComparison.OrdinalIgnoreCase),
            _ => null);

        Assert.Equal(@"C:\custom\bash.exe", result);
    }

    [Fact]
    public void FindGitBashPathWindowsOrThrow_Throws_For_Missing_Configured_Path()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => FindGitBashPathWindowsOrThrow(
            @"C:\custom\missing-bash.exe",
            @"C:\repo",
            static _ => false,
            _ => null));

        var actual = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(
            "Claude Code was unable to find CLAUDE_CODE_GIT_BASH_PATH path \"C:\\custom\\missing-bash.exe\"",
            actual.Message);
    }

    [Fact]
    public void FindGitBashPathWindowsOrThrow_Throws_When_Git_Bash_Cannot_Be_Found()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => FindGitBashPathWindowsOrThrow(
            null,
            @"C:\repo",
            static _ => false,
            _ => null));

        var actual = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(
            "Claude Code on Windows requires git-bash (https://git-scm.com/downloads/win). If installed but not in PATH, set environment variable pointing to your bash.exe, similar to: CLAUDE_CODE_GIT_BASH_PATH=C:\\Program Files\\Git\\bin\\bash.exe",
            actual.Message);
    }

    private static string? FindExecutableOnWindows(
        string executable,
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> runWhere)
    {
        var method = typeof(BashShellDetection).GetMethod(
            "FindExecutableOnWindows",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        return (string?)method.Invoke(null, [executable, currentWorkingDirectory, pathExists, runWhere]);
    }

    private static string? TryFindGitBashPathWindows(
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> findExecutable)
    {
        var method = typeof(BashShellDetection).GetMethod(
            "TryFindGitBashPathWindows",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        return (string?)method.Invoke(null, [currentWorkingDirectory, pathExists, findExecutable]);
    }

    private static string FindGitBashPathWindowsOrThrow(
        string? configuredGitBashPath,
        string currentWorkingDirectory,
        Func<string, bool> pathExists,
        Func<string, string?> findExecutable)
    {
        var method = typeof(BashShellDetection).GetMethod(
            "FindGitBashPathWindowsOrThrow",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null,
            [
                typeof(string),
                typeof(string),
                typeof(Func<string, bool>),
                typeof(Func<string, string?>)
            ],
            null)!;

        return (string)method.Invoke(null, [configuredGitBashPath, currentWorkingDirectory, pathExists, findExecutable])!;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
