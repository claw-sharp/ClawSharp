// TS origin: ./utils/shell/powershellProvider.ts
namespace ClawSharp.Tasks;

public sealed class PowerShellShellProvider
{
    private string? _currentSandboxTmpDir;

    public PowerShellShellProvider(string shellPath)
    {
        ShellPath = shellPath;
    }

    public string ShellPath { get; }

    public bool Detached => false;

    public static string[] BuildPowerShellArgs(string command)
    {
        return
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command
        ];
    }

    public PowerShellExecCommand BuildExecCommand(
        string command,
        string id,
        bool useSandbox = false,
        string? sandboxTmpDir = null)
    {
        _currentSandboxTmpDir = useSandbox ? sandboxTmpDir : null;

        var cwdFilePath = useSandbox && !string.IsNullOrWhiteSpace(sandboxTmpDir)
            ? CombinePosix(sandboxTmpDir, $"claude-pwd-ps-{id}")
            : Path.Combine(Path.GetTempPath(), $"claude-pwd-ps-{id}");
        var escapedCwdFilePath = cwdFilePath.Replace("'", "''", StringComparison.Ordinal);

        var cwdTracking =
            "\n; $_ec = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } elseif ($?) { 0 } else { 1 }\n" +
            $"; (Get-Location).Path | Out-File -FilePath '{escapedCwdFilePath}' -Encoding utf8 -NoNewline\n" +
            "; exit $_ec";
        var powerShellCommand = command + cwdTracking;

        var commandString = useSandbox
            ? string.Join(
                ' ',
                [
                    QuotePosixSingle(ShellPath),
                    "-NoProfile",
                    "-NonInteractive",
                    "-EncodedCommand",
                    EncodePowerShellCommand(powerShellCommand)
                ])
            : powerShellCommand;

        return new PowerShellExecCommand(commandString, cwdFilePath);
    }

    public string[] GetSpawnArguments(string commandString)
    {
        return BuildPowerShellArgs(commandString);
    }

    public IReadOnlyDictionary<string, string> GetEnvironmentOverrides(
        IReadOnlyDictionary<string, string>? sessionEnvironmentVariables = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (sessionEnvironmentVariables is not null)
        {
            foreach (var pair in sessionEnvironmentVariables)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(_currentSandboxTmpDir))
        {
            environment["TMPDIR"] = _currentSandboxTmpDir;
            environment["CLAUDE_CODE_TMPDIR"] = _currentSandboxTmpDir;
        }

        return environment;
    }

    private static string EncodePowerShellCommand(string powerShellCommand)
    {
        return Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(powerShellCommand));
    }

    private static string QuotePosixSingle(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private static string CombinePosix(string root, string child)
    {
        var trimmedRoot = root.TrimEnd('/');
        return $"{trimmedRoot}/{child}";
    }
}

public sealed record PowerShellExecCommand(string CommandString, string CwdFilePath);
