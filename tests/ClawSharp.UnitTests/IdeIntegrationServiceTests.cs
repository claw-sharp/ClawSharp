// TS origin: ./utils/ide.ts, ./commands/ide/ide.tsx
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class IdeIntegrationServiceTests
{
    [Fact]
    public async Task DetectIdesAsync_Parses_Legacy_Lockfile_Format()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
        var configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".claude");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(configRoot, "ide"));

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "ide", "12348.lock"),
                workspaceRoot + Environment.NewLine);

            var service = new IdeIntegrationService(workspaceRoot);

            var detected = await service.DetectIdesAsync(includeInvalid: true);

            var ide = Assert.Single(detected);
            Assert.Equal("IDE", ide.Name);
            Assert.True(ide.IsValid);
            Assert.Equal(12348, ide.Port);
            Assert.Equal($"http://127.0.0.1:{ide.Port}/sse", ide.Url);
            Assert.Null(ide.CliCommand);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(Path.GetDirectoryName(workspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(workspaceRoot)!, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(configRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(configRoot)!, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DetectIdesAsync_Parses_Json_Lockfiles_And_Marks_Workspace_Validity()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
        var configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".claude");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(configRoot, "ide"));

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "ide", "12345.lock"),
                """
                {
                  "workspaceFolders": ["REPLACE_WORKSPACE"],
                  "ideName": "VS Code",
                  "transport": "sse"
                }
                """.Replace("REPLACE_WORKSPACE", workspaceRoot.Replace("\\", "\\\\"), StringComparison.Ordinal));

            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "ide", "12346.lock"),
                """
                {
                  "workspaceFolders": ["C:\\other-workspace"],
                  "ideName": "Cursor",
                  "transport": "ws"
                }
                """);

            var service = new IdeIntegrationService(workspaceRoot);

            var detected = await service.DetectIdesAsync(includeInvalid: true);

            Assert.Equal(2, detected.Count);
            var expectedVsCodeCommand = OperatingSystem.IsWindows() ? "code.cmd" : "code";
            var expectedCursorCommand = OperatingSystem.IsWindows() ? "cursor.cmd" : "cursor";
            Assert.Contains(detected, ide => ide.Name == "VS Code" && ide.IsValid && ide.Port == 12345 && ide.CliCommand == expectedVsCodeCommand);
            Assert.Contains(detected, ide => ide.Name == "Cursor" && !ide.IsValid && ide.Port == 12346 && ide.Url == "ws://127.0.0.1:12346" && ide.CliCommand == expectedCursorCommand);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(Path.GetDirectoryName(workspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(workspaceRoot)!, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(configRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(configRoot)!, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DetectIdesAsync_In_Wsl_Uses_Windows_Home_Lockfiles_Path_Conversion_And_Gateway_Host()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        var configRoot = Path.Combine(tempRoot, ".claude");
        var windowsUsersRoot = Path.Combine(tempRoot, "Users");
        var windowsHomeOnDisk = Path.Combine(windowsUsersRoot, "TestUser");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(configRoot, "ide"));
        Directory.CreateDirectory(Path.Combine(windowsHomeOnDisk, ".claude", "ide"));

        var lockfilePath = Path.Combine(windowsHomeOnDisk, ".claude", "ide", "22334.lock");
        await File.WriteAllTextAsync(
            lockfilePath,
            """
            {
              "workspaceFolders": ["C:\\Users\\TestUser\\workspace"],
              "ideName": "VS Code",
              "transport": "ws",
              "runningInWindows": true
            }
            """);

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            var service = new IdeIntegrationService(
                workspaceRoot,
                executeAsync: (fileName, args, _) => Task.FromResult(
                    string.Equals(fileName, "ip", StringComparison.Ordinal)
                        ? new ProcessExecutionResult(0, "default via 172.22.224.1 dev eth0\n", string.Empty)
                        : new ProcessExecutionResult(-1, string.Empty, "unexpected")),
                getEnvironmentVariable: name => name switch
                {
                    "WSL_DISTRO_NAME" => "Ubuntu",
                    _ => null
                },
                isWslRuntime: () => true,
                pathConverterFactory: _ => new FakeWindowsToWslPathConverter(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [@"C:\Users\TestUser"] = windowsHomeOnDisk,
                        [@"C:\Users\TestUser\workspace"] = workspaceRoot
                    }),
                getWindowsUserProfileAsync: _ => Task.FromResult<string?>(@"C:\Users\TestUser"),
                checkIdeConnectionAsync: (host, port, _) => Task.FromResult(
                    string.Equals(host, "172.22.224.1", StringComparison.Ordinal) && port == 22334),
                windowsUsersRoot: windowsUsersRoot);

            var detected = await service.DetectIdesAsync(includeInvalid: true);

            var expectedVsCodeCommand = OperatingSystem.IsWindows() ? "code.cmd" : "code";
            Assert.Single(detected);
            Assert.Contains(
                detected,
                ide => ide.Name == "VS Code" &&
                       ide.IsValid &&
                       ide.Port == 22334 &&
                       ide.Url == "ws://172.22.224.1:22334" &&
                       ide.RunningInWindows &&
                       ide.CliCommand == expectedVsCodeCommand);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DetectIdesAsync_In_Wsl_Rejects_Wsl_Unc_Paths_From_Different_Distro()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
        var configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".claude");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(configRoot, "ide"));

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "ide", "12347.lock"),
                """
                {
                  "workspaceFolders": ["\\\\wsl$\\DifferentDistro\\home\\user\\workspace"],
                  "ideName": "Cursor",
                  "transport": "sse",
                  "runningInWindows": true
                }
                """);

            var service = new IdeIntegrationService(
                workspaceRoot,
                getEnvironmentVariable: name => name switch
                {
                    "WSL_DISTRO_NAME" => "Ubuntu",
                    _ => Environment.GetEnvironmentVariable(name)
                },
                isWslRuntime: () => true);

            var detected = await service.DetectIdesAsync(includeInvalid: true);

            Assert.Single(detected);
            Assert.Contains(detected, ide => ide.Name == "Cursor" && !ide.IsValid && ide.RunningInWindows);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(Path.GetDirectoryName(workspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(workspaceRoot)!, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(configRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(configRoot)!, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenInIdeAsync_Uses_Cli_Command_And_Returns_Success_Message()
    {
        string? executedFileName = null;
        IReadOnlyList<string>? executedArguments = null;
        var service = new IdeIntegrationService(
            Environment.CurrentDirectory,
            executeAsync: (fileName, args, _) =>
            {
                executedFileName = fileName;
                executedArguments = args.ToArray();
                return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            });

        var result = await service.OpenInIdeAsync(
            @"D:\repo\file.txt",
            new DetectedIdeInfo("VS Code", 12345, [@"D:\repo"], "ws://127.0.0.1:12345", true, "code.cmd"));

        Assert.True(result.Success);
        Assert.Equal("Opened D:\\repo\\file.txt in VS Code.", result.Message);
        Assert.Equal("code.cmd", executedFileName);
        Assert.Equal([@"D:\repo\file.txt"], executedArguments);
    }

    [Fact]
    public async Task OpenInIdeAsync_Returns_Manual_Open_Message_When_Cli_Command_Is_Missing()
    {
        var service = new IdeIntegrationService(Environment.CurrentDirectory);

        var result = await service.OpenInIdeAsync(
            @"D:\repo\file.txt",
            new DetectedIdeInfo("Custom IDE", 12345, [@"D:\repo"], "ws://127.0.0.1:12345", true));

        Assert.False(result.Success);
        Assert.Equal("Please open manually in Custom IDE: D:\\repo\\file.txt", result.Message);
    }

    [Fact]
    public async Task OpenInIdeAsync_Returns_Failure_Message_When_Cli_Command_Fails()
    {
        var service = new IdeIntegrationService(
            Environment.CurrentDirectory,
            executeAsync: static (_, _, _) => Task.FromResult(new ProcessExecutionResult(1, string.Empty, "boom")));

        var result = await service.OpenInIdeAsync(
            @"D:\repo\file.txt",
            new DetectedIdeInfo("Cursor", 12345, [@"D:\repo"], "ws://127.0.0.1:12345", true, "cursor.cmd"));

        Assert.False(result.Success);
        Assert.Equal("Failed to open in Cursor. Try opening manually: D:\\repo\\file.txt", result.Message);
    }

    private sealed class FakeWindowsToWslPathConverter(IReadOnlyDictionary<string, string> mappings) : IWindowsToWslPathConverter
    {
        public string ToLocalPath(string idePath)
        {
            return mappings.TryGetValue(idePath, out var converted)
                ? converted
                : idePath;
        }

        public string ToIdePath(string localPath)
        {
            foreach (var pair in mappings)
            {
                if (string.Equals(pair.Value, localPath, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            return localPath;
        }
    }
}
