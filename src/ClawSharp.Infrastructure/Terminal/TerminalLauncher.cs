using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClawSharp.Infrastructure;

public sealed record TerminalInfo(string Name, string Command);

public sealed record TerminalLaunchRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool WindowsVerbatimArguments = false);

public sealed class TerminalLauncher
{
    private static readonly IReadOnlyList<MacOsTerminalDefinition> MacOsTerminals =
    [
        new("iTerm2", "com.googlecode.iterm2", "iTerm"),
        new("Ghostty", "com.mitchellh.ghostty", "Ghostty"),
        new("Kitty", "net.kovidgoyal.kitty", "kitty"),
        new("Alacritty", "org.alacritty", "Alacritty"),
        new("WezTerm", "com.github.wez.wezterm", "WezTerm"),
        new("Terminal.app", "com.apple.Terminal", "Terminal")
    ];

    private static readonly IReadOnlyList<string> LinuxTerminals =
    [
        "ghostty",
        "kitty",
        "alacritty",
        "wezterm",
        "gnome-terminal",
        "konsole",
        "xfce4-terminal",
        "mate-terminal",
        "tilix",
        "xterm"
    ];

    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;
    private readonly Func<TerminalLaunchRequest, CancellationToken, Task<bool>> _startDetachedAsync;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<CancellationToken, Task<TerminalInfo?>> _detectTerminalAsync;
    private readonly string _platform;
    private readonly string _globalConfigPath;

    public TerminalLauncher(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null,
        Func<TerminalLaunchRequest, CancellationToken, Task<bool>>? startDetachedAsync = null,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<CancellationToken, Task<TerminalInfo?>>? detectTerminalAsync = null,
        string? platform = null,
        string? globalConfigPath = null)
    {
        _executeAsync = executeAsync ?? ((fileName, args, token) =>
            ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token));
        _startDetachedAsync = startDetachedAsync ?? StartDetachedAsync;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _platform = platform ?? GetPlatform();
        _globalConfigPath = globalConfigPath ?? ClaudeConfigPaths.GetGlobalClaudeFilePath();
        _detectTerminalAsync = detectTerminalAsync ?? DetectTerminalAsync;
    }

    public Task<TerminalInfo?> DetectTerminalAsync(CancellationToken cancellationToken = default)
    {
        return _platform switch
        {
            "darwin" => DetectMacOsTerminalAsync(cancellationToken),
            "linux" => DetectLinuxTerminalAsync(cancellationToken),
            "win32" => DetectWindowsTerminalAsync(cancellationToken),
            _ => Task.FromResult<TerminalInfo?>(null)
        };
    }

    public async Task<bool> LaunchAsync(
        string claudePath,
        IReadOnlyList<string> claudeArgs,
        string? cwd,
        CancellationToken cancellationToken = default)
    {
        var terminal = await _detectTerminalAsync(cancellationToken).ConfigureAwait(false);
        if (terminal is null)
        {
            return false;
        }

        return _platform switch
        {
            "darwin" => await LaunchMacOsTerminalAsync(terminal, claudePath, claudeArgs, cwd, cancellationToken).ConfigureAwait(false),
            "linux" => await LaunchLinuxTerminalAsync(terminal, claudePath, claudeArgs, cwd, cancellationToken).ConfigureAwait(false),
            "win32" => await LaunchWindowsTerminalAsync(terminal, claudePath, claudeArgs, cwd, cancellationToken).ConfigureAwait(false),
            _ => false
        };
    }

    private async Task<TerminalInfo?> DetectMacOsTerminalAsync(CancellationToken cancellationToken)
    {
        var stored = ReadStoredDeepLinkTerminal();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var storedMatch = MacOsTerminals.FirstOrDefault(
                terminal => string.Equals(terminal.App, stored, StringComparison.Ordinal));
            if (storedMatch is not null)
            {
                return new TerminalInfo(storedMatch.Name, storedMatch.App);
            }
        }

        var termProgram = _getEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrWhiteSpace(termProgram))
        {
            var normalized = termProgram.Replace(".app", string.Empty, StringComparison.OrdinalIgnoreCase);
            var envMatch = MacOsTerminals.FirstOrDefault(
                terminal =>
                    string.Equals(terminal.App, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(terminal.Name, normalized, StringComparison.OrdinalIgnoreCase));
            if (envMatch is not null)
            {
                return new TerminalInfo(envMatch.Name, envMatch.App);
            }
        }

        foreach (var terminal in MacOsTerminals)
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var result = await _executeAsync(
                "mdfind",
                [$"kMDItemCFBundleIdentifier == \"{terminal.BundleId}\""],
                linkedSource.Token).ConfigureAwait(false);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
            {
                return new TerminalInfo(terminal.Name, terminal.App);
            }
        }

        foreach (var terminal in MacOsTerminals)
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var result = await _executeAsync(
                "ls",
                [$"/Applications/{terminal.App}.app"],
                linkedSource.Token).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return new TerminalInfo(terminal.Name, terminal.App);
            }
        }

        return new TerminalInfo("Terminal.app", "Terminal");
    }

    private async Task<TerminalInfo?> DetectLinuxTerminalAsync(CancellationToken cancellationToken)
    {
        var terminalFromEnvironment = _getEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrWhiteSpace(terminalFromEnvironment))
        {
            var resolved = await ResolveOnPathAsync(terminalFromEnvironment).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return new TerminalInfo(Path.GetFileName(terminalFromEnvironment), resolved);
            }
        }

        var xTerminalEmulator = await ResolveOnPathAsync("x-terminal-emulator").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(xTerminalEmulator))
        {
            return new TerminalInfo("x-terminal-emulator", xTerminalEmulator);
        }

        foreach (var terminal in LinuxTerminals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveOnPathAsync(terminal).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return new TerminalInfo(terminal, resolved);
            }
        }

        return null;
    }

    private async Task<TerminalInfo?> DetectWindowsTerminalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wt = await ResolveOnPathAsync("wt.exe").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(wt))
        {
            return new TerminalInfo("Windows Terminal", wt);
        }

        var pwsh = await ResolveOnPathAsync("pwsh.exe").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(pwsh))
        {
            return new TerminalInfo("PowerShell", pwsh);
        }

        var powershell = await ResolveOnPathAsync("powershell.exe").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(powershell))
        {
            return new TerminalInfo("PowerShell", powershell);
        }

        return new TerminalInfo("Command Prompt", "cmd.exe");
    }

    private async Task<bool> LaunchMacOsTerminalAsync(
        TerminalInfo terminal,
        string claudePath,
        IReadOnlyList<string> claudeArgs,
        string? cwd,
        CancellationToken cancellationToken)
    {
        switch (terminal.Command)
        {
            case "iTerm":
            {
                var shellCommand = BuildShellCommand(claudePath, claudeArgs, cwd);
                var script = $"""
tell application "iTerm"
  if running then
    create window with default profile
  else
    activate
  end if
  tell current session of current window
    write text {AppleScriptQuote(shellCommand)}
  end tell
end tell
""";
                var result = await _executeAsync("osascript", ["-e", script], cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }

                break;
            }
            case "Terminal":
            {
                var shellCommand = BuildShellCommand(claudePath, claudeArgs, cwd);
                var script = $"""
tell application "Terminal"
  do script {AppleScriptQuote(shellCommand)}
  activate
end tell
""";
                var result = await _executeAsync("osascript", ["-e", script], cancellationToken).ConfigureAwait(false);
                return result.ExitCode == 0;
            }
            case "Ghostty":
            {
                var arguments = new List<string> { "-na", terminal.Command, "--args", "--window-save-state=never" };
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add($"--working-directory={cwd}");
                }

                arguments.Add("-e");
                arguments.Add(claudePath);
                arguments.AddRange(claudeArgs);
                var result = await _executeAsync("open", arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }

                break;
            }
            case "Alacritty":
            {
                var arguments = new List<string> { "-na", terminal.Command, "--args" };
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--working-directory");
                    arguments.Add(cwd);
                }

                arguments.Add("-e");
                arguments.Add(claudePath);
                arguments.AddRange(claudeArgs);
                var result = await _executeAsync("open", arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }

                break;
            }
            case "kitty":
            {
                var arguments = new List<string> { "-na", terminal.Command, "--args" };
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--directory");
                    arguments.Add(cwd);
                }

                arguments.Add(claudePath);
                arguments.AddRange(claudeArgs);
                var result = await _executeAsync("open", arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }

                break;
            }
            case "WezTerm":
            {
                var arguments = new List<string> { "-na", terminal.Command, "--args", "start" };
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--cwd");
                    arguments.Add(cwd);
                }

                arguments.Add("--");
                arguments.Add(claudePath);
                arguments.AddRange(claudeArgs);
                var result = await _executeAsync("open", arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }

                break;
            }
        }

        if (string.Equals(terminal.Command, "Terminal", StringComparison.Ordinal))
        {
            return false;
        }

        return await LaunchMacOsTerminalAsync(
            new TerminalInfo("Terminal.app", "Terminal"),
            claudePath,
            claudeArgs,
            cwd,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> LaunchLinuxTerminalAsync(
        TerminalInfo terminal,
        string claudePath,
        IReadOnlyList<string> claudeArgs,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>();
        string? spawnCwd = null;

        switch (terminal.Name)
        {
            case "gnome-terminal":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add($"--working-directory={cwd}");
                }

                arguments.Add("--");
                break;
            case "konsole":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--workdir");
                    arguments.Add(cwd);
                }

                arguments.Add("-e");
                break;
            case "kitty":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--directory");
                    arguments.Add(cwd);
                }

                break;
            case "wezterm":
                arguments.Add("start");
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--cwd");
                    arguments.Add(cwd);
                }

                arguments.Add("--");
                break;
            case "alacritty":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("--working-directory");
                    arguments.Add(cwd);
                }

                arguments.Add("-e");
                break;
            case "ghostty":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add($"--working-directory={cwd}");
                }

                arguments.Add("-e");
                break;
            case "xfce4-terminal":
            case "mate-terminal":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add($"--working-directory={cwd}");
                }

                arguments.Add("-x");
                break;
            case "tilix":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add($"--working-directory={cwd}");
                }

                arguments.Add("-e");
                break;
            default:
                arguments.Add("-e");
                spawnCwd = cwd;
                break;
        }

        arguments.Add(claudePath);
        arguments.AddRange(claudeArgs);
        return await _startDetachedAsync(
            new TerminalLaunchRequest(terminal.Command, arguments, spawnCwd),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> LaunchWindowsTerminalAsync(
        TerminalInfo terminal,
        string claudePath,
        IReadOnlyList<string> claudeArgs,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>();
        switch (terminal.Name)
        {
            case "Windows Terminal":
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    arguments.Add("-d");
                    arguments.Add(cwd);
                }

                arguments.Add("--");
                arguments.Add(claudePath);
                arguments.AddRange(claudeArgs);
                break;
            case "PowerShell":
            {
                var cdCommand = string.IsNullOrWhiteSpace(cwd)
                    ? string.Empty
                    : $"Set-Location {PowerShellQuote(cwd)}; ";
                arguments.Add("-NoExit");
                arguments.Add("-Command");
                arguments.Add($"{cdCommand}& {PowerShellQuote(claudePath)} {string.Join(" ", claudeArgs.Select(PowerShellQuote))}");
                break;
            }
            default:
            {
                var cdCommand = string.IsNullOrWhiteSpace(cwd)
                    ? string.Empty
                    : $"cd /d {CmdQuote(cwd)} && ";
                arguments.Add("/k");
                arguments.Add($"{cdCommand}{CmdQuote(claudePath)} {string.Join(" ", claudeArgs.Select(CmdQuote))}");
                break;
            }
        }

        return await _startDetachedAsync(
            new TerminalLaunchRequest(
                terminal.Command,
                arguments,
                WindowsVerbatimArguments: string.Equals(terminal.Name, "Command Prompt", StringComparison.Ordinal)),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveOnPathAsync(string executableName)
    {
        if (Path.IsPathRooted(executableName))
        {
            return File.Exists(executableName) ? executableName : null;
        }

        var pathValue = _getEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? ReadStoredDeepLinkTerminal()
    {
        try
        {
            if (!File.Exists(_globalConfigPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_globalConfigPath));
            return document.RootElement.TryGetProperty("deepLinkTerminal", out var element) &&
                   element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win32";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "darwin";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "unknown";
    }

    private static string BuildShellCommand(string claudePath, IReadOnlyList<string> claudeArgs, string? cwd)
    {
        var cdPrefix = string.IsNullOrWhiteSpace(cwd)
            ? string.Empty
            : $"cd {ShellQuote(cwd)} && ";
        return $"{cdPrefix}{string.Join(" ", new[] { claudePath }.Concat(claudeArgs).Select(ShellQuote))}";
    }

    private static string ShellQuote(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private static string AppleScriptQuote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string PowerShellQuote(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static string CmdQuote(string value)
    {
        var stripped = value.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        var escaped = Regex.Replace(stripped, @"(\\+)$", "$1$1", RegexOptions.CultureInvariant);
        return $"\"{escaped}\"";
    }

    private static Task<bool> StartDetachedAsync(TerminalLaunchRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                startInfo.WorkingDirectory = request.WorkingDirectory;
            }

            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // .NET does not expose Node's windowsVerbatimArguments toggle on ProcessStartInfo.
            // The C# port keeps the TS-shaped request flag for tests and future refinement,
            // while relying on the explicit cmd.exe command-string quoting above today.

            using var process = Process.Start(startInfo);
            return Task.FromResult(process is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private sealed record MacOsTerminalDefinition(string Name, string BundleId, string App);
}
