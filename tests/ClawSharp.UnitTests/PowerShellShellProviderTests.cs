using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class PowerShellShellProviderTests
{
    [Fact]
    public void BuildPowerShellArgs_Returns_Ts_Shaped_Flag_Set()
    {
        var args = PowerShellShellProvider.BuildPowerShellArgs("Get-Process");

        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-Command", "Get-Process"],
            args);
    }

    [Fact]
    public void BuildExecCommand_Appends_Cwd_Tracking_And_Exit_Code_Shaping()
    {
        var provider = new PowerShellShellProvider("pwsh");

        var result = provider.BuildExecCommand("Get-ChildItem", "42");

        Assert.StartsWith("Get-ChildItem", result.CommandString, StringComparison.Ordinal);
        Assert.Contains("$_ec = if ($null -ne $LASTEXITCODE)", result.CommandString, StringComparison.Ordinal);
        Assert.Contains("(Get-Location).Path | Out-File -FilePath", result.CommandString, StringComparison.Ordinal);
        Assert.EndsWith("; exit $_ec", result.CommandString, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine(Path.GetTempPath(), "claude-pwd-ps-42"), result.CwdFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExecCommand_Uses_Encoded_Command_For_Sandbox_Mode()
    {
        var provider = new PowerShellShellProvider("/opt/microsoft/powershell/7/pwsh");

        var result = provider.BuildExecCommand("Write-Output 'hello'", "abc", useSandbox: true, sandboxTmpDir: "/tmp/claude");

        Assert.StartsWith("'/opt/microsoft/powershell/7/pwsh' -NoProfile -NonInteractive -EncodedCommand ", result.CommandString, StringComparison.Ordinal);
        Assert.Equal("/tmp/claude/claude-pwd-ps-abc", result.CwdFilePath);
    }

    [Fact]
    public void GetEnvironmentOverrides_Preserves_Session_Env_And_Adds_Sandbox_Tmp()
    {
        var provider = new PowerShellShellProvider("pwsh");
        provider.BuildExecCommand("Get-Date", "env", useSandbox: true, sandboxTmpDir: "/tmp/claude");

        var environment = provider.GetEnvironmentOverrides(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/bin",
                ["FOO"] = "bar"
            });

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("bar", environment["FOO"]);
        Assert.Equal("/tmp/claude", environment["TMPDIR"]);
        Assert.Equal("/tmp/claude", environment["CLAUDE_CODE_TMPDIR"]);
    }

    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", PowerShellEdition.Core)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", PowerShellEdition.Desktop)]
    [InlineData(@"/usr/bin/pwsh", PowerShellEdition.Core)]
    [InlineData(@"/usr/bin/powershell", PowerShellEdition.Desktop)]
    public void InferEdition_Uses_Binary_Name_Parity(string path, PowerShellEdition expected)
    {
        Assert.Equal(expected, PowerShellDetection.InferEdition(path));
    }

    [Theory]
    [InlineData("findstr foo file.txt", 1, false, "No matches found")]
    [InlineData("findstr foo file.txt", 2, true, null)]
    [InlineData("robocopy src dst", 0, false, "No files copied (already in sync)")]
    [InlineData("robocopy src dst", 1, false, "Files copied successfully")]
    [InlineData("robocopy src dst", 2, false, "Robocopy completed (no errors)")]
    [InlineData("robocopy src dst", 8, true, null)]
    [InlineData("& \"C:\\tools\\rg.exe\" foo .", 1, false, "No matches found")]
    public void InterpretPowerShellCommandResult_Matches_Ts_Command_Semantics(
        string command,
        int exitCode,
        bool expectedIsError,
        string? expectedMessage)
    {
        var interpretation = InterpretPowerShell(command, exitCode);

        Assert.Equal(expectedIsError, interpretation.IsError);
        Assert.Equal(expectedMessage, interpretation.Message);
    }

    [Theory]
    [InlineData("Start-Sleep -Seconds 1", false)]
    [InlineData("sleep 1", false)]
    [InlineData("Get-Date", true)]
    [InlineData("& \"C:\\tools\\pwsh.exe\" -NoProfile", true)]
    public void PowerShellAutobackgroundingAllowance_Matches_Ts_Disallowlist(string command, bool expected)
    {
        Assert.Equal(expected, IsPowerShellAutoBackgroundingAllowed(command));
    }

    [Fact]
    public async Task LinuxSnapPwshFallback_Prefers_Direct_Binary_When_Resolved_Path_Is_Snap()
    {
        var resolvedPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/usr/bin/pwsh"] = "/snap/bin/pwsh",
            ["/opt/microsoft/powershell/7/pwsh"] = "/opt/microsoft/powershell/7/pwsh"
        };
        var probedPaths = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["/opt/microsoft/powershell/7/pwsh"] = "/opt/microsoft/powershell/7/pwsh",
            ["/usr/bin/pwsh"] = "/usr/bin/pwsh"
        };

        var result = await PreferDirectLinuxPwshBinaryIfSnapLauncherAsync(
            "/usr/bin/pwsh",
            path => resolvedPaths.TryGetValue(path, out var resolved) ? resolved : path,
            path => Task.FromResult(probedPaths.TryGetValue(path, out var found) ? found : null));

        Assert.Equal("/opt/microsoft/powershell/7/pwsh", result);
    }

    [Fact]
    public async Task LinuxSnapPwshFallback_Keeps_Original_When_Direct_Binary_Also_Resolves_To_Snap()
    {
        var result = await PreferDirectLinuxPwshBinaryIfSnapLauncherAsync(
            "/snap/bin/pwsh",
            path => path switch
            {
                "/snap/bin/pwsh" => "/snap/bin/pwsh",
                "/opt/microsoft/powershell/7/pwsh" => "/snap/pwsh/current",
                _ => path
            },
            path => Task.FromResult(path == "/opt/microsoft/powershell/7/pwsh" ? "/opt/microsoft/powershell/7/pwsh" : null));

        Assert.Equal("/snap/bin/pwsh", result);
    }

    [Fact]
    public async Task FindPowerShellAsync_OnLinux_Prefers_Pwsh_On_Path()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-linux-pwsh-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var pwshPath = Path.Combine(tempRoot, "pwsh");
        var powershellPath = Path.Combine(tempRoot, "powershell");
        await File.WriteAllTextAsync(pwshPath, string.Empty);
        await File.WriteAllTextAsync(powershellPath, string.Empty);

        using var pathScope = new EnvironmentVariableScope("PATH", tempRoot);
        PowerShellDetection.ResetPowerShellCache();

        try
        {
            var result = await PowerShellDetection.FindPowerShellAsync();

            Assert.Equal(pwshPath, result);
        }
        finally
        {
            PowerShellDetection.ResetPowerShellCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FindPowerShellAsync_OnLinux_Falls_Back_To_Powershell_When_Pwsh_Is_Missing()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-linux-pwsh-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var powershellPath = Path.Combine(tempRoot, "powershell");
        await File.WriteAllTextAsync(powershellPath, string.Empty);

        using var pathScope = new EnvironmentVariableScope("PATH", tempRoot);
        PowerShellDetection.ResetPowerShellCache();

        try
        {
            var result = await PowerShellDetection.FindPowerShellAsync();

            Assert.Equal(powershellPath, result);
        }
        finally
        {
            PowerShellDetection.ResetPowerShellCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FindPowerShellAsync_OnLinux_Returns_Null_When_Path_Does_Not_Contain_PowerShell()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-linux-pwsh-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        using var pathScope = new EnvironmentVariableScope("PATH", tempRoot);
        PowerShellDetection.ResetPowerShellCache();

        try
        {
            var result = await PowerShellDetection.FindPowerShellAsync();

            Assert.Null(result);
        }
        finally
        {
            PowerShellDetection.ResetPowerShellCache();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static (bool IsError, string? Message) InterpretPowerShell(string command, int exitCode)
    {
        var toolsAssembly = typeof(ToolRegistry).Assembly;
        var shellToolKindType = toolsAssembly.GetType("ClawSharp.Tools.ShellToolKind", throwOnError: true)!;
        var semanticsType = toolsAssembly.GetType("ClawSharp.Tools.ShellCommandSemantics", throwOnError: true)!;
        var interpretationType = toolsAssembly.GetType("ClawSharp.Tools.ShellCommandInterpretation", throwOnError: true)!;
        var powerShellKind = Enum.Parse(shellToolKindType, "PowerShell");
        var interpretMethod = semanticsType.GetMethod(
            "Interpret",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

        var interpretation = interpretMethod.Invoke(null, [powerShellKind, command, exitCode, string.Empty, string.Empty])!;
        var isError = Assert.IsType<bool>(interpretationType.GetProperty("IsError")!.GetValue(interpretation));
        var rawMessage = interpretationType.GetProperty("Message")!.GetValue(interpretation);
        var message = rawMessage is null ? null : Assert.IsType<string>(rawMessage);
        return (isError, message);
    }

    private static bool IsPowerShellAutoBackgroundingAllowed(string command)
    {
        var toolsAssembly = typeof(ToolRegistry).Assembly;
        var shellToolKindType = toolsAssembly.GetType("ClawSharp.Tools.ShellToolKind", throwOnError: true)!;
        var semanticsType = toolsAssembly.GetType("ClawSharp.Tools.ShellCommandSemantics", throwOnError: true)!;
        var powerShellKind = Enum.Parse(shellToolKindType, "PowerShell");
        var method = semanticsType.GetMethod(
            "IsAutoBackgroundingAllowed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

        return Assert.IsType<bool>(method.Invoke(null, [powerShellKind, command]));
    }

    private static async Task<string> PreferDirectLinuxPwshBinaryIfSnapLauncherAsync(
        string pwshPath,
        Func<string, string> resolvePath,
        Func<string, Task<string?>> probePathAsync)
    {
        var detectionType = typeof(PowerShellDetection);
        var method = detectionType.GetMethod(
            "PreferDirectLinuxPwshBinaryIfSnapLauncherAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(null, [pwshPath, resolvePath, probePathAsync]));
        await task.ConfigureAwait(false);
        return Assert.IsType<string>(task.GetType().GetProperty("Result")!.GetValue(task));
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
