using System.Text.Json;
using System.Text.RegularExpressions;
using ClawSharp.Core;
using System.Net.Sockets;
using System.Text;

namespace ClawSharp.Infrastructure;

public sealed record DetectedIdeInfo(
    string Name,
    int Port,
    IReadOnlyList<string> WorkspaceFolders,
    string Url,
    bool IsValid,
    string? CliCommand = null,
    string? AuthToken = null,
    bool RunningInWindows = false);

internal sealed record IdeLockfileInfo(
    IReadOnlyList<string> WorkspaceFolders,
    int Port,
    string? IdeName,
    bool UseWebSocket,
    bool RunningInWindows,
    string? AuthToken);

public sealed class IdeIntegrationService
{
    private static readonly Regex DefaultRoutePattern = new(
        @"default via (\d+\.\d+\.\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> CliCommands =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VS Code"] = "code",
            ["Visual Studio Code"] = "code",
            ["Cursor"] = "cursor",
            ["Windsurf"] = "windsurf"
        };

    private readonly string _workspaceRoot;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<bool> _isWslRuntime;
    private readonly Func<string?, IWindowsToWslPathConverter> _pathConverterFactory;
    private readonly Func<CancellationToken, Task<string?>> _getWindowsUserProfileAsync;
    private readonly Func<string, int, CancellationToken, Task<bool>> _checkIdeConnectionAsync;
    private readonly string _windowsUsersRoot;

    public IdeIntegrationService(
        string workspaceRoot,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<bool>? isWslRuntime = null,
        Func<string?, IWindowsToWslPathConverter>? pathConverterFactory = null,
        Func<CancellationToken, Task<string?>>? getWindowsUserProfileAsync = null,
        Func<string, int, CancellationToken, Task<bool>>? checkIdeConnectionAsync = null,
        string? windowsUsersRoot = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _executeAsync = executeAsync ?? ((fileName, args, token) =>
            ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token));
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _isWslRuntime = isWslRuntime ?? IsWslRuntime;
        _pathConverterFactory = pathConverterFactory ?? (distroName => new WindowsToWslPathConverter(distroName));
        _getWindowsUserProfileAsync = getWindowsUserProfileAsync ?? ResolveWindowsUserProfileAsync;
        _checkIdeConnectionAsync = checkIdeConnectionAsync ?? CheckIdeConnectionAsync;
        _windowsUsersRoot = windowsUsersRoot ?? "/mnt/c/Users";
    }

    public async Task<IReadOnlyList<DetectedIdeInfo>> DetectIdesAsync(
        bool includeInvalid,
        CancellationToken cancellationToken = default)
    {
        var lockfiles = await GetSortedIdeLockfilesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DetectedIdeInfo>();

        foreach (var lockfilePath in lockfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lockfile = await ReadIdeLockfileAsync(lockfilePath, cancellationToken).ConfigureAwait(false);
            if (lockfile is null)
            {
                continue;
            }

            var isValid = lockfile.WorkspaceFolders.Any(path => IsWorkspaceMatch(path, lockfile.RunningInWindows));
            if (!includeInvalid && !isValid)
            {
                continue;
            }

            var host = await DetectHostIpAsync(lockfile.RunningInWindows, lockfile.Port, cancellationToken).ConfigureAwait(false);
            var url = lockfile.UseWebSocket
                ? $"ws://{host}:{lockfile.Port}"
                : $"http://{host}:{lockfile.Port}/sse";
            var ideName = string.IsNullOrWhiteSpace(lockfile.IdeName) ? "IDE" : lockfile.IdeName!;

            result.Add(
                new DetectedIdeInfo(
                    ideName,
                    lockfile.Port,
                    lockfile.WorkspaceFolders,
                    url,
                    isValid,
                    ResolveCliCommand(ideName),
                    lockfile.AuthToken,
                    lockfile.RunningInWindows));
        }

        return result;
    }

    public async Task<(bool Success, string Message)> OpenInIdeAsync(
        string targetPath,
        DetectedIdeInfo ide,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ide.CliCommand))
        {
            return (false, $"Please open manually in {ide.Name}: {targetPath}");
        }

        var result = await _executeAsync(ide.CliCommand, [targetPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return (true, $"Opened {targetPath} in {ide.Name}.");
        }

        return (false, $"Failed to open in {ide.Name}. Try opening manually: {targetPath}");
    }

    private static string GetIdeLockfilesDirectory()
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "ide");
    }

    private async Task<IReadOnlyList<string>> GetSortedIdeLockfilesAsync(CancellationToken cancellationToken)
    {
        var directories = await GetIdeLockfileDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => directories
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.lock", SearchOption.TopDirectoryOnly))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetIdeLockfileDirectoriesAsync(CancellationToken cancellationToken)
    {
        HashSet<string> directories = [GetIdeLockfilesDirectory()];
        if (!_isWslRuntime())
        {
            return directories.ToArray();
        }

        var wslDistroName = _getEnvironmentVariable("WSL_DISTRO_NAME");
        var windowsHome = await _getWindowsUserProfileAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(windowsHome))
        {
            var convertedHome = _pathConverterFactory(wslDistroName).ToLocalPath(windowsHome);
            if (!string.IsNullOrWhiteSpace(convertedHome))
            {
                directories.Add(Path.GetFullPath(Path.Combine(convertedHome, ".claude", "ide")));
            }
        }

        try
        {
            if (!Directory.Exists(_windowsUsersRoot))
            {
                return directories.ToArray();
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(_windowsUsersRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    continue;
                }

                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                var isSymlink = attributes.HasFlag(FileAttributes.ReparsePoint);
                if (!isDirectory && !isSymlink)
                {
                    continue;
                }

                var userDirectoryName = Path.GetFileName(entry);
                if (string.Equals(userDirectoryName, "Public", StringComparison.Ordinal) ||
                    string.Equals(userDirectoryName, "Default", StringComparison.Ordinal) ||
                    string.Equals(userDirectoryName, "Default User", StringComparison.Ordinal) ||
                    string.Equals(userDirectoryName, "All Users", StringComparison.Ordinal))
                {
                    continue;
                }

                directories.Add(Path.Combine(entry, ".claude", "ide"));
            }
        }
        catch
        {
            // WSL Windows drive discovery is best effort, matching the TS behavior.
        }

        return directories.ToArray();
    }

    private static async Task<IdeLockfileInfo?> ReadIdeLockfileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var portText = Path.GetFileNameWithoutExtension(filePath);
            if (!int.TryParse(portText, out var port))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;
                var workspaceFolders = root.TryGetProperty("workspaceFolders", out var foldersElement) &&
                                       foldersElement.ValueKind == JsonValueKind.Array
                    ? foldersElement.EnumerateArray()
                        .Where(static entry => entry.ValueKind == JsonValueKind.String)
                        .Select(static entry => entry.GetString()!)
                        .ToArray()
                    : [];

                var ideName = root.TryGetProperty("ideName", out var ideNameElement) &&
                              ideNameElement.ValueKind == JsonValueKind.String
                    ? ideNameElement.GetString()
                    : null;
                var useWebSocket = root.TryGetProperty("transport", out var transportElement) &&
                                   string.Equals(transportElement.GetString(), "ws", StringComparison.Ordinal);
                var runningInWindows = root.TryGetProperty("runningInWindows", out var runningElement) &&
                                       runningElement.ValueKind == JsonValueKind.True;
                var authToken = root.TryGetProperty("authToken", out var tokenElement) &&
                                tokenElement.ValueKind == JsonValueKind.String
                    ? tokenElement.GetString()
                    : null;

                return new IdeLockfileInfo(workspaceFolders, port, ideName, useWebSocket, runningInWindows, authToken);
            }
            catch (JsonException)
            {
                var workspaceFolders = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new IdeLockfileInfo(workspaceFolders, port, null, false, false, null);
            }
        }
        catch
        {
            return null;
        }
    }

    private bool IsWorkspaceMatch(string workspaceFolder, bool runningInWindows)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
        {
            return false;
        }

        var normalizedRoot = NormalizePathForComparison(_workspaceRoot);
        if (_isWslRuntime() && runningInWindows)
        {
            var wslDistroName = _getEnvironmentVariable("WSL_DISTRO_NAME");
            if (!string.IsNullOrWhiteSpace(wslDistroName) &&
                !WindowsToWslPathConverter.CheckWslDistroMatch(workspaceFolder, wslDistroName))
            {
                return false;
            }

            var originalWorkspace = NormalizePathForComparison(workspaceFolder);
            if (IsPathWithin(normalizedRoot, originalWorkspace))
            {
                return true;
            }

            workspaceFolder = _pathConverterFactory(wslDistroName).ToLocalPath(workspaceFolder);
        }

        var normalizedWorkspace = NormalizePathForComparison(workspaceFolder);
        return IsPathWithin(normalizedRoot, normalizedWorkspace);
    }

    private async Task<string?> ResolveWindowsUserProfileAsync(CancellationToken cancellationToken)
    {
        var userProfile = _getEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return userProfile;
        }

        var result = await _executeAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "$env:USERPROFILE"],
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout)
            ? result.Stdout.Trim()
            : null;
    }

    private async Task<string> DetectHostIpAsync(bool ideRunningInWindows, int port, CancellationToken cancellationToken)
    {
        var hostOverride = _getEnvironmentVariable("CLAUDE_CODE_IDE_HOST_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(hostOverride))
        {
            return hostOverride;
        }

        if (!_isWslRuntime() || !ideRunningInWindows)
        {
            return "127.0.0.1";
        }

        var result = await _executeAsync("ip", ["route", "show"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            foreach (Match match in DefaultRoutePattern.Matches(result.Stdout))
            {
                var gatewayIp = match.Groups[1].Value;
                if (await _checkIdeConnectionAsync(gatewayIp, port, cancellationToken).ConfigureAwait(false))
                {
                    return gatewayIp;
                }
            }
        }

        return "127.0.0.1";
    }

    private static async Task<bool> CheckIdeConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(500));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWslRuntime()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var procVersion = File.ReadAllText("/proc/version", Encoding.UTF8);
            return procVersion.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                   procVersion.Contains("wsl", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPathWithin(string candidatePath, string workspacePath)
    {
        var comparison = GetPathComparison();
        return string.Equals(candidatePath, workspacePath, comparison) ||
               candidatePath.StartsWith(workspacePath + Path.DirectorySeparatorChar, comparison) ||
               candidatePath.StartsWith(workspacePath + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string NormalizePathForComparison(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .Normalize(NormalizationForm.FormC)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Normalize(NormalizationForm.FormC)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static string? ResolveCliCommand(string ideName)
    {
        if (!CliCommands.TryGetValue(ideName, out var cliCommand))
        {
            return null;
        }

        return OperatingSystem.IsWindows()
            ? cliCommand + ".cmd"
            : cliCommand;
    }
}
