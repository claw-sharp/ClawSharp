using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class TerminalLauncherTests
{
    [Fact]
    public async Task DetectTerminalAsync_Uses_Linux_Terminal_Environment_Override()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-terminal-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var terminalPath = Path.Combine(tempRoot, "custom-terminal");
        await File.WriteAllTextAsync(terminalPath, string.Empty);

        try
        {
            var launcher = new TerminalLauncher(
                getEnvironmentVariable: name => name switch
                {
                    "TERMINAL" => terminalPath,
                    _ => null
                },
                platform: "linux");

            var terminal = await launcher.DetectTerminalAsync();

            Assert.NotNull(terminal);
            Assert.Equal("custom-terminal", terminal!.Name);
            Assert.Equal(terminalPath, terminal.Command);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DetectTerminalAsync_Falls_Back_To_XTerminalEmulator_On_Linux()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-terminal-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var pathEntry = Path.Combine(tempRoot, "bin");
        Directory.CreateDirectory(pathEntry);
        var terminalPath = Path.Combine(pathEntry, "x-terminal-emulator");
        await File.WriteAllTextAsync(terminalPath, string.Empty);

        try
        {
            var launcher = new TerminalLauncher(
                getEnvironmentVariable: name => name switch
                {
                    "PATH" => pathEntry,
                    _ => null
                },
                platform: "linux");

            var terminal = await launcher.DetectTerminalAsync();

            Assert.NotNull(terminal);
            Assert.Equal("x-terminal-emulator", terminal!.Name);
            Assert.Equal(terminalPath, terminal.Command);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_Uses_Linux_GnomeTerminal_WorkingDirectory_Arguments()
    {
        TerminalLaunchRequest? request = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("gnome-terminal", "/usr/bin/gnome-terminal")),
            startDetachedAsync: (launchRequest, _) =>
            {
                request = launchRequest;
                return Task.FromResult(true);
            },
            platform: "linux");

        var launched = await launcher.LaunchAsync("/usr/bin/clawsharp", ["repl", "--deep-link-origin"], "/workspace");

        Assert.True(launched);
        Assert.NotNull(request);
        Assert.Equal("/usr/bin/gnome-terminal", request!.FileName);
        Assert.Null(request.WorkingDirectory);
        Assert.Equal(
            ["--working-directory=/workspace", "--", "/usr/bin/clawsharp", "repl", "--deep-link-origin"],
            request.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_Uses_Linux_Konsole_Workdir_Arguments()
    {
        TerminalLaunchRequest? request = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("konsole", "/usr/bin/konsole")),
            startDetachedAsync: (launchRequest, _) =>
            {
                request = launchRequest;
                return Task.FromResult(true);
            },
            platform: "linux");

        var launched = await launcher.LaunchAsync("/usr/bin/clawsharp", ["repl"], "/workspace");

        Assert.True(launched);
        Assert.NotNull(request);
        Assert.Equal("/usr/bin/konsole", request!.FileName);
        Assert.Null(request.WorkingDirectory);
        Assert.Equal(
            ["--workdir", "/workspace", "-e", "/usr/bin/clawsharp", "repl"],
            request.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_Uses_Linux_WezTerm_Start_Arguments()
    {
        TerminalLaunchRequest? request = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("wezterm", "/usr/bin/wezterm")),
            startDetachedAsync: (launchRequest, _) =>
            {
                request = launchRequest;
                return Task.FromResult(true);
            },
            platform: "linux");

        var launched = await launcher.LaunchAsync("/usr/bin/clawsharp", ["repl"], "/workspace");

        Assert.True(launched);
        Assert.NotNull(request);
        Assert.Equal("/usr/bin/wezterm", request!.FileName);
        Assert.Null(request.WorkingDirectory);
        Assert.Equal(
            ["start", "--cwd", "/workspace", "--", "/usr/bin/clawsharp", "repl"],
            request.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_Uses_Linux_Default_Terminal_Spawn_WorkingDirectory()
    {
        TerminalLaunchRequest? request = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("xterm", "/usr/bin/xterm")),
            startDetachedAsync: (launchRequest, _) =>
            {
                request = launchRequest;
                return Task.FromResult(true);
            },
            platform: "linux");

        var launched = await launcher.LaunchAsync("/usr/bin/clawsharp", ["repl"], "/workspace");

        Assert.True(launched);
        Assert.NotNull(request);
        Assert.Equal("/usr/bin/xterm", request!.FileName);
        Assert.Equal("/workspace", request.WorkingDirectory);
        Assert.Equal(
            ["-e", "/usr/bin/clawsharp", "repl"],
            request.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_Uses_Windows_CommandPrompt_With_Ts_Shaped_Cmd_Quoting()
    {
        TerminalLaunchRequest? request = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("Command Prompt", "cmd.exe")),
            startDetachedAsync: (launchRequest, _) =>
            {
                request = launchRequest;
                return Task.FromResult(true);
            },
            platform: "win32");

        var launched = await launcher.LaunchAsync(
            @"C:\Program Files\ClawSharp\ClawSharp.exe",
            ["repl", "--deep-link-origin", "--deep-link-draft", "say \"hello\" %PATH%"],
            "C:\\work\\repo\\");

        Assert.True(launched);
        Assert.NotNull(request);
        Assert.Equal("cmd.exe", request!.FileName);
        Assert.True(request.WindowsVerbatimArguments);
        Assert.Equal("/k", request.Arguments[0]);
        Assert.Equal(
            "cd /d \"C:\\work\\repo\\\\\" && \"C:\\Program Files\\ClawSharp\\ClawSharp.exe\" \"repl\" \"--deep-link-origin\" \"--deep-link-draft\" \"say hello %%PATH%%\"",
            request.Arguments[1]);
    }

    [Fact]
    public async Task LaunchAsync_Uses_MacOs_WezTerm_Open_Arguments()
    {
        string? executedFileName = null;
        IReadOnlyList<string>? executedArguments = null;
        var launcher = new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(new TerminalInfo("WezTerm", "WezTerm")),
            executeAsync: (fileName, args, _) =>
            {
                executedFileName = fileName;
                executedArguments = args.ToArray();
                return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            },
            platform: "darwin");

        var launched = await launcher.LaunchAsync(
            "/Applications/ClawSharp.app/Contents/MacOS/ClawSharp",
            ["repl"],
            "/Users/test/project");

        Assert.True(launched);
        Assert.Equal("open", executedFileName);
        Assert.Equal(
            [
                "-na",
                "WezTerm",
                "--args",
                "start",
                "--cwd",
                "/Users/test/project",
                "--",
                "/Applications/ClawSharp.app/Contents/MacOS/ClawSharp",
                "repl"
            ],
            executedArguments);
    }
}
