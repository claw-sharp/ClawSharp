using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

public sealed class LocalShellProcessRunnerTests
{
    [Fact]
    public async Task StartAsync_Merges_Stdout_And_Stderr_Into_File_Mode_Output()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-shell-process-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new TaskOutput("processmerge", outputPath, stdoutToFile: true, managedFileWrites: true);
        var runner = new LocalShellProcessRunner();

        var command = CreateEchoStartInfo(taskOutput, "stdout-line", "stderr-line");
        var shellCommand = await runner.StartAsync(command);
        var result = await shellCommand.Result;
        var normalized = result.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(0, result.Code);
        Assert.Contains("stdout-line", normalized, StringComparison.Ordinal);
        Assert.Contains("stderr-line", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("[stderr]", normalized, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.True(taskOutput.OutputFileRedundant);
    }

    [Fact]
    public async Task StartAsync_Returns_Ts_Shaped_Timeout_Result()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-shell-timeout-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new TaskOutput("processtimeout", outputPath, stdoutToFile: true, managedFileWrites: true);
        var runner = new LocalShellProcessRunner();

        var command = CreateSleepStartInfo(taskOutput, timeoutMs: 100);
        var shellCommand = await runner.StartAsync(command);
        var result = await shellCommand.Result;

        Assert.Equal(143, result.Code);
        Assert.Equal("Command timed out after 100ms", result.Stderr);
    }

    [Fact]
    public async Task StartAsync_Uses_WorkingDirectory_And_Environment_Overrides()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-shell-env-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new TaskOutput("processenv", outputPath, stdoutToFile: true, managedFileWrites: true);
        var runner = new LocalShellProcessRunner();
        var workingDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-shell-cwd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var command = CreateEnvironmentEchoStartInfo(taskOutput, workingDirectory, "CLAWSHARP_TEST_ENV", "ts-parity");
            var shellCommand = await runner.StartAsync(command);
            var result = await shellCommand.Result;
            var normalized = result.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(0, result.Code);
            Assert.Contains(workingDirectory.Replace('\\', '/'), normalized.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ts-parity", normalized, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_Kill_Interrupts_Process_And_Returns_SigKill_Shape()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-shell-kill-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new TaskOutput("processkill", outputPath, stdoutToFile: true, managedFileWrites: true);
        var runner = new LocalShellProcessRunner();

        var command = CreateSleepStartInfo(taskOutput, timeoutMs: 10_000);
        var shellCommand = await runner.StartAsync(command);
        await Task.Delay(200);
        shellCommand.Kill();

        var result = await shellCommand.Result;

        Assert.Equal(137, result.Code);
        Assert.True(result.Interrupted);
    }

    [Fact]
    public async Task StartAsync_Returns_Ts_Shaped_Spawn_Failure_Result()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "clawsharp-shell-spawn-failure-tests", Guid.NewGuid().ToString("N"), "output.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var taskOutput = new TaskOutput("processspawnfail", outputPath, stdoutToFile: true, managedFileWrites: true);
        var runner = new LocalShellProcessRunner();

        var shellCommand = await runner.StartAsync(
            new LocalShellProcessStartInfo(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-shell.exe"),
                [],
                Environment.CurrentDirectory,
                taskOutput,
                1_000));

        var result = await shellCommand.Result;

        Assert.Equal(126, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
    }

    private static LocalShellProcessStartInfo CreateEchoStartInfo(TaskOutput taskOutput, string stdoutText, string stderrText)
    {
        if (OperatingSystem.IsWindows())
        {
            return new LocalShellProcessStartInfo(
                "cmd.exe",
                ["/d", "/c", $"echo {stdoutText} & echo {stderrText} 1>&2"],
                Environment.CurrentDirectory,
                taskOutput,
                10_000);
        }

        return new LocalShellProcessStartInfo(
            "/bin/sh",
            ["-c", $"printf '%s\\n' '{stdoutText}'; printf '%s\\n' '{stderrText}' 1>&2"],
            Environment.CurrentDirectory,
            taskOutput,
            10_000);
    }

    private static LocalShellProcessStartInfo CreateSleepStartInfo(TaskOutput taskOutput, int timeoutMs)
    {
        if (OperatingSystem.IsWindows())
        {
            return new LocalShellProcessStartInfo(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 5"],
                Environment.CurrentDirectory,
                taskOutput,
                timeoutMs);
        }

        return new LocalShellProcessStartInfo(
            "/bin/sh",
            ["-c", "sleep 5"],
            Environment.CurrentDirectory,
            taskOutput,
            timeoutMs);
    }

    private static LocalShellProcessStartInfo CreateEnvironmentEchoStartInfo(
        TaskOutput taskOutput,
        string workingDirectory,
        string variableName,
        string variableValue)
    {
        IReadOnlyList<string> arguments;
        if (OperatingSystem.IsWindows())
        {
            arguments =
            [
                "/d",
                "/c",
                $"echo %CD% & echo %{variableName}%"
            ];

            return new LocalShellProcessStartInfo(
                "cmd.exe",
                arguments,
                workingDirectory,
                taskOutput,
                10_000,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [variableName] = variableValue
                });
        }

        arguments =
        [
            "-c",
            $"printf '%s\\n' \"$PWD\" \"${variableName}\""
        ];

        return new LocalShellProcessStartInfo(
            "/bin/sh",
            arguments,
            workingDirectory,
            taskOutput,
            10_000,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [variableName] = variableValue
            });
    }
}
