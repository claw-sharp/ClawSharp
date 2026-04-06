// TS origin: ./utils/desktopDeepLink.ts
using System.Runtime.InteropServices;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed record DesktopInstallStatus(string Status, string? Version = null);

public sealed record DesktopOpenResult(
    bool Success,
    string? Error = null,
    string? DeepLinkUrl = null);

public sealed class DesktopDeepLinkService
{
    public const string MinimumDesktopVersion = "1.1.2396";

    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;

    public DesktopDeepLinkService(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null)
    {
        _executeAsync = executeAsync ?? ((fileName, args, token) =>
            ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token));
    }

    public string GetDownloadUrl()
    {
        return GetDownloadUrl(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            RuntimeInformation.ProcessArchitecture);
    }

    public async Task<DesktopInstallStatus> GetInstallStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsDesktopInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DesktopInstallStatus("not-installed");
        }

        string? version;
        try
        {
            version = await GetDesktopVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new DesktopInstallStatus("ready", "unknown");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return new DesktopInstallStatus("ready", "unknown");
        }

        if (!IsVersionAtLeast(version, MinimumDesktopVersion))
        {
            return new DesktopInstallStatus("version-too-old", version);
        }

        return new DesktopInstallStatus("ready", version);
    }

    public static string GetDownloadUrl(bool isWindows, bool isMacOs, Architecture processArchitecture)
    {
        return isWindows && processArchitecture == Architecture.X64
            ? "https://claude.ai/api/desktop/win32/x64/exe/latest/redirect"
            : isMacOs
                ? "https://claude.ai/api/desktop/darwin/universal/dmg/latest/redirect"
                : "https://claude.ai/download";
    }

    public string BuildDesktopDeepLink(string sessionId, string cwd)
    {
        var protocol = IsDevMode() ? "claude-dev" : "claude";
        var url = new UriBuilder($"{protocol}://resume");
        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        query["session"] = sessionId;
        query["cwd"] = cwd;
        url.Query = query.ToString() ?? string.Empty;
        return url.Uri.ToString();
    }

    public async Task<DesktopOpenResult> OpenCurrentSessionInDesktopAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default)
    {
        if (!await IsDesktopInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DesktopOpenResult(
                false,
                "Claude Desktop is not installed. Install it from https://claude.ai/download");
        }

        var deepLinkUrl = BuildDesktopDeepLink(session.Id, session.ProjectDirectory);
        var opened = await OpenDeepLinkAsync(deepLinkUrl, cancellationToken).ConfigureAwait(false);
        if (!opened)
        {
            return new DesktopOpenResult(
                false,
                "Failed to open Claude Desktop. Please try opening it manually.",
                deepLinkUrl);
        }

        return new DesktopOpenResult(true, DeepLinkUrl: deepLinkUrl);
    }

    private async Task<bool> IsDesktopInstalledAsync(CancellationToken cancellationToken)
    {
        if (IsDevMode())
        {
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            return IsMacOsDesktopInstalled(Directory.Exists);
        }

        if (OperatingSystem.IsLinux())
        {
            var result = await _executeAsync(
                "xdg-mime",
                ["query", "default", "x-scheme-handler/claude"],
                cancellationToken).ConfigureAwait(false);
            return IsLinuxProtocolHandlerRegistered(result);
        }

        if (OperatingSystem.IsWindows())
        {
            var result = await _executeAsync(
                "reg",
                ["query", @"HKEY_CLASSES_ROOT\claude", "/ve"],
                cancellationToken).ConfigureAwait(false);
            return IsWindowsProtocolHandlerRegistered(result);
        }

        return false;
    }

    private async Task<string?> GetDesktopVersionAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            var result = await _executeAsync(
                "defaults",
                ["read", "/Applications/Claude.app/Contents/Info.plist", "CFBundleShortVersionString"],
                cancellationToken).ConfigureAwait(false);
            return ParseMacOsDesktopVersion(result);
        }

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsDesktopVersion(
                Environment.GetEnvironmentVariable("LOCALAPPDATA"),
                Directory.Exists,
                installDirectory => Directory.EnumerateDirectories(installDirectory, "app-*", SearchOption.TopDirectoryOnly));
        }

        return null;
    }

    private async Task<bool> OpenDeepLinkAsync(string deepLinkUrl, CancellationToken cancellationToken)
    {
        var invocation = GetOpenDeepLinkCommand(
            deepLinkUrl,
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux(),
            OperatingSystem.IsWindows(),
            IsDevMode());
        if (invocation is null)
        {
            return false;
        }

        var (fileName, arguments) = invocation.Value;
        var result = await _executeAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static bool IsDevMode()
    {
        return IsDevMode(
            Environment.GetEnvironmentVariable("NODE_ENV"),
            [Environment.GetCommandLineArgs().FirstOrDefault(), Environment.ProcessPath]);
    }

    public static bool IsSupportedPlatform()
    {
        return IsSupportedPlatform(
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsWindows(),
            RuntimeInformation.ProcessArchitecture);
    }

    public static bool IsSupportedPlatform(bool isMacOs, bool isWindows, Architecture processArchitecture)
    {
        if (isMacOs)
        {
            return true;
        }

        return isWindows && processArchitecture == Architecture.X64;
    }

    public static bool IsDevMode(string? nodeEnvironment, IEnumerable<string?> pathsToCheck)
    {
        if (string.Equals(nodeEnvironment, "development", StringComparison.Ordinal))
        {
            return true;
        }

        string[] buildDirectories =
        [
            "/build-ant/",
            "/build-ant-native/",
            "/build-external/",
            "/build-external-native/"
        ];

        foreach (var path in pathsToCheck)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = path.Replace('\\', '/');
            if (buildDirectories.Any(normalizedPath.Contains))
            {
                return true;
            }
        }

        return false;
    }

    public static (string FileName, IReadOnlyList<string> Arguments)? GetOpenDeepLinkCommand(
        string deepLinkUrl,
        bool isMacOs,
        bool isLinux,
        bool isWindows,
        bool isDevMode)
    {
        if (isMacOs)
        {
            return isDevMode
                ? (
                    "osascript",
                    [
                        "-e",
                        $"tell application \"Electron\" to open location \"{deepLinkUrl}\""
                    ])
                : ("open", [deepLinkUrl]);
        }

        if (isLinux)
        {
            return ("xdg-open", [deepLinkUrl]);
        }

        if (isWindows)
        {
            return ("cmd", ["/c", "start", "", deepLinkUrl]);
        }

        return null;
    }

    public static bool IsVersionAtLeast(string actualVersion, string minimumVersion)
    {
        return TryNormalizeVersion(actualVersion, out var actual) &&
               TryNormalizeVersion(minimumVersion, out var minimum) &&
               actual >= minimum;
    }

    private static bool TryNormalizeVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        var digits = new List<int>();
        foreach (var part in value.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            var numericPrefix = new string(part.TakeWhile(char.IsDigit).ToArray());
            if (numericPrefix.Length == 0 || !int.TryParse(numericPrefix, out var parsed))
            {
                break;
            }

            digits.Add(parsed);
        }

        if (digits.Count == 0)
        {
            return false;
        }

        while (digits.Count < 4)
        {
            digits.Add(0);
        }

        version = new Version(digits[0], digits[1], digits[2], digits[3]);
        return true;
    }

    private static Version? NormalizeVersion(string value)
    {
        return TryNormalizeVersion(value, out var version) ? version : null;
    }

    private static bool IsLinuxProtocolHandlerRegistered(ProcessExecutionResult result)
    {
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout);
    }

    private static bool IsMacOsDesktopInstalled(Func<string, bool> directoryExists)
    {
        return directoryExists("/Applications/Claude.app");
    }

    private static bool IsWindowsProtocolHandlerRegistered(ProcessExecutionResult result)
    {
        return result.ExitCode == 0;
    }

    private static string? ParseMacOsDesktopVersion(ProcessExecutionResult result)
    {
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout)
            ? result.Stdout.Trim()
            : null;
    }

    private static string? GetWindowsDesktopVersion(
        string? localAppData,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        var installDirectory = Path.Combine(localAppData, "AnthropicClaude");
        if (!directoryExists(installDirectory))
        {
            return null;
        }

        var versions = enumerateDirectories(installDirectory)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Where(static name => name!.StartsWith("app-", StringComparison.Ordinal))
            .Select(static name => name![4..])
            .Where(static version => TryNormalizeVersion(version, out _))
            .Select(static version => new { Raw = version, Normalized = NormalizeVersion(version)! })
            .OrderBy(static entry => entry.Normalized)
            .ToArray();

        return versions.LastOrDefault()?.Raw;
    }
}
