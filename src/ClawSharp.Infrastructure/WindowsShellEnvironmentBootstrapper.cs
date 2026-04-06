// TS origin: ./entrypoints/init.ts, ./utils/windowsPaths.ts
using ClawSharp.Tasks;

namespace ClawSharp.Infrastructure;

public static class WindowsShellEnvironmentBootstrapper
{
    public static void Initialize()
    {
        Initialize(
            OperatingSystem.IsWindows(),
            static () => BashShellDetection.FindGitBashPathWindowsOrThrow(),
            Environment.SetEnvironmentVariable);
    }

    public static void Initialize(
        bool isWindows,
        Func<string> resolveGitBashPath,
        Action<string, string?> setEnvironmentVariable)
    {
        if (!isWindows)
        {
            return;
        }

        setEnvironmentVariable("SHELL", resolveGitBashPath());
    }
}
