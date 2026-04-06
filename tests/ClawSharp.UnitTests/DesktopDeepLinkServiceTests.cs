// TS origin: ./utils/desktopDeepLink.ts
using System.Runtime.InteropServices;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class DesktopDeepLinkServiceTests
{
    [Theory]
    [InlineData(true, false, Architecture.X64, "https://claude.ai/api/desktop/win32/x64/exe/latest/redirect")]
    [InlineData(true, false, Architecture.Arm64, "https://claude.ai/download")]
    [InlineData(false, true, Architecture.X64, "https://claude.ai/api/desktop/darwin/universal/dmg/latest/redirect")]
    [InlineData(false, false, Architecture.X64, "https://claude.ai/download")]
    public void GetDownloadUrl_Matches_Current_Platform_Branches(
        bool isWindows,
        bool isMacOs,
        Architecture architecture,
        string expected)
    {
        Assert.Equal(expected, DesktopDeepLinkService.GetDownloadUrl(isWindows, isMacOs, architecture));
    }

    [Fact]
    public void BuildDesktopDeepLink_Encodes_Session_And_Cwd()
    {
        var service = new DesktopDeepLinkService();

        var deepLink = service.BuildDesktopDeepLink("session-123", @"C:\work tree");

        Assert.Contains("session=session-123", deepLink, StringComparison.Ordinal);
        Assert.Contains("cwd=", deepLink, StringComparison.Ordinal);
        Assert.True(
            deepLink.StartsWith("claude://", StringComparison.OrdinalIgnoreCase) ||
            deepLink.StartsWith("claude-dev://", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true, false, Architecture.X64, true)]
    [InlineData(false, true, Architecture.X64, true)]
    [InlineData(false, true, Architecture.Arm64, false)]
    [InlineData(false, false, Architecture.X64, false)]
    public void IsSupportedPlatform_Matches_Ts_Command_Gate(
        bool isMacOs,
        bool isWindows,
        Architecture architecture,
        bool expected)
    {
        Assert.Equal(expected, DesktopDeepLinkService.IsSupportedPlatform(isMacOs, isWindows, architecture));
    }

    [Theory]
    [InlineData("development", "", "", true)]
    [InlineData(null, "/tmp/build-ant/output/cli.js", "", true)]
    [InlineData(null, "", "/tmp/build-external-native/app", true)]
    [InlineData("production", "/tmp/release/cli.js", "/tmp/release/dotnet", false)]
    public void IsDevMode_Matches_Ts_Rules(
        string? nodeEnvironment,
        string? firstPath,
        string? secondPath,
        bool expected)
    {
        Assert.Equal(expected, DesktopDeepLinkService.IsDevMode(nodeEnvironment, [firstPath, secondPath]));
    }

    [Fact]
    public void GetOpenDeepLinkCommand_Uses_OsaScript_For_Mac_Dev_Mode()
    {
        var invocation = DesktopDeepLinkService.GetOpenDeepLinkCommand(
            "claude-dev://resume?session=1",
            isMacOs: true,
            isLinux: false,
            isWindows: false,
            isDevMode: true);

        Assert.NotNull(invocation);
        Assert.Equal("osascript", invocation.Value.FileName);
        Assert.Equal(
            [
                "-e",
                "tell application \"Electron\" to open location \"claude-dev://resume?session=1\""
            ],
            invocation.Value.Arguments);
    }

    [Fact]
    public void GetOpenDeepLinkCommand_Uses_Open_For_Mac_Production()
    {
        var invocation = DesktopDeepLinkService.GetOpenDeepLinkCommand(
            "claude://resume?session=1",
            isMacOs: true,
            isLinux: false,
            isWindows: false,
            isDevMode: false);

        Assert.NotNull(invocation);
        Assert.Equal("open", invocation.Value.FileName);
        Assert.Equal(["claude://resume?session=1"], invocation.Value.Arguments);
    }

    [Fact]
    public void GetOpenDeepLinkCommand_Uses_XdgOpen_For_Linux()
    {
        var invocation = DesktopDeepLinkService.GetOpenDeepLinkCommand(
            "claude://resume?session=1",
            isMacOs: false,
            isLinux: true,
            isWindows: false,
            isDevMode: false);

        Assert.NotNull(invocation);
        Assert.Equal("xdg-open", invocation.Value.FileName);
        Assert.Equal(["claude://resume?session=1"], invocation.Value.Arguments);
    }

    [Fact]
    public void LinuxProtocolHandlerRegistration_Requires_NonEmpty_Stdout()
    {
        Assert.False(IsLinuxProtocolHandlerRegistered(new ProcessExecutionResult(0, "", "")));
        Assert.True(IsLinuxProtocolHandlerRegistered(new ProcessExecutionResult(0, "claude.desktop\n", "")));
        Assert.False(IsLinuxProtocolHandlerRegistered(new ProcessExecutionResult(1, "claude.desktop\n", "")));
    }

    [Fact]
    public void MacOsDesktopInstallDetection_Uses_Applications_Claude_App_Path()
    {
        Assert.True(IsMacOsDesktopInstalled(path => string.Equals(path, "/Applications/Claude.app", StringComparison.Ordinal)));
        Assert.False(IsMacOsDesktopInstalled(static _ => false));
    }

    [Fact]
    public void WindowsProtocolHandlerRegistration_Uses_ExitCode_Only()
    {
        Assert.True(IsWindowsProtocolHandlerRegistered(new ProcessExecutionResult(0, "", "missing")));
        Assert.False(IsWindowsProtocolHandlerRegistered(new ProcessExecutionResult(1, "Claude", "")));
    }

    [Fact]
    public void ParseMacOsDesktopVersion_Trims_Successful_Defaults_Output()
    {
        var version = ParseMacOsDesktopVersion(new ProcessExecutionResult(0, "1.2.3 \n", ""));

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void ParseMacOsDesktopVersion_Returns_Null_For_Failure_Or_Blank_Output()
    {
        Assert.Null(ParseMacOsDesktopVersion(new ProcessExecutionResult(1, "1.2.3", "")));
        Assert.Null(ParseMacOsDesktopVersion(new ProcessExecutionResult(0, " \n", "")));
    }

    [Fact]
    public void GetWindowsDesktopVersion_Selects_Highest_Semver_Like_App_Directory()
    {
        var localAppData = @"C:\Users\tester\AppData\Local";
        var result = GetWindowsDesktopVersion(
            localAppData,
            path => string.Equals(path, Path.Combine(localAppData, "AnthropicClaude"), StringComparison.OrdinalIgnoreCase),
            _ =>
            [
                Path.Combine(localAppData, "AnthropicClaude", "app-1.1.2396"),
                Path.Combine(localAppData, "AnthropicClaude", "notes"),
                Path.Combine(localAppData, "AnthropicClaude", "app-1.2.0-beta.1"),
                Path.Combine(localAppData, "AnthropicClaude", "app-broken"),
                Path.Combine(localAppData, "AnthropicClaude", "app-1.10.0")
            ]);

        Assert.Equal("1.10.0", result);
    }

    [Fact]
    public void GetWindowsDesktopVersion_Returns_Null_When_Install_Directory_Is_Missing()
    {
        var result = GetWindowsDesktopVersion(
            @"C:\Users\tester\AppData\Local",
            static _ => false,
            static _ => Array.Empty<string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInstallStatusAsync_Windows_Returns_NotInstalled_When_Protocol_Handler_Is_Missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new DesktopDeepLinkService(
            executeAsync: static (fileName, _, _) =>
            {
                Assert.Equal("reg", fileName);
                return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "missing"));
            });

        var status = await service.GetInstallStatusAsync();

        Assert.Equal("not-installed", status.Status);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetInstallStatusAsync_Windows_Returns_VersionTooOld_When_Squirrel_Install_Is_Below_Minimum()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var localAppData = Path.Combine(Path.GetTempPath(), "clawsharp-desktop-tests", Guid.NewGuid().ToString("N"));
        var installDirectory = Path.Combine(localAppData, "AnthropicClaude", "app-1.1.2395");
        Directory.CreateDirectory(installDirectory);

        try
        {
            using var _ = new EnvironmentVariableScope("LOCALAPPDATA", localAppData);
            var service = new DesktopDeepLinkService(
                executeAsync: static (_, _, _) => Task.FromResult(new ProcessExecutionResult(0, "Claude", string.Empty)));

            var status = await service.GetInstallStatusAsync();

            Assert.Equal("version-too-old", status.Status);
            Assert.Equal("1.1.2395", status.Version);
        }
        finally
        {
            Directory.Delete(localAppData, recursive: true);
        }
    }

    [Fact]
    public async Task GetInstallStatusAsync_Windows_Returns_Ready_When_Squirrel_Install_Meets_Minimum()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var localAppData = Path.Combine(Path.GetTempPath(), "clawsharp-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(localAppData, "AnthropicClaude", "app-1.1.2396"));
        Directory.CreateDirectory(Path.Combine(localAppData, "AnthropicClaude", "app-1.2.0"));

        try
        {
            using var _ = new EnvironmentVariableScope("LOCALAPPDATA", localAppData);
            var service = new DesktopDeepLinkService(
                executeAsync: static (_, _, _) => Task.FromResult(new ProcessExecutionResult(0, "Claude", string.Empty)));

            var status = await service.GetInstallStatusAsync();

            Assert.Equal("ready", status.Status);
            Assert.Equal("1.2.0", status.Version);
        }
        finally
        {
            Directory.Delete(localAppData, recursive: true);
        }
    }

    [Fact]
    public async Task GetInstallStatusAsync_Windows_Returns_Ready_Unknown_When_Version_Cannot_Be_Determined()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var localAppData = Path.Combine(Path.GetTempPath(), "clawsharp-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(localAppData, "AnthropicClaude"));

        try
        {
            using var _ = new EnvironmentVariableScope("LOCALAPPDATA", localAppData);
            var service = new DesktopDeepLinkService(
                executeAsync: static (_, _, _) => Task.FromResult(new ProcessExecutionResult(0, "Claude", string.Empty)));

            var status = await service.GetInstallStatusAsync();

            Assert.Equal("ready", status.Status);
            Assert.Equal("unknown", status.Version);
        }
        finally
        {
            Directory.Delete(localAppData, recursive: true);
        }
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

    private static bool IsLinuxProtocolHandlerRegistered(ProcessExecutionResult result)
    {
        return InvokePrivateStatic<bool>("IsLinuxProtocolHandlerRegistered", result);
    }

    private static bool IsWindowsProtocolHandlerRegistered(ProcessExecutionResult result)
    {
        return InvokePrivateStatic<bool>("IsWindowsProtocolHandlerRegistered", result);
    }

    private static bool IsMacOsDesktopInstalled(Func<string, bool> directoryExists)
    {
        return InvokePrivateStatic<bool>("IsMacOsDesktopInstalled", directoryExists);
    }

    private static string? ParseMacOsDesktopVersion(ProcessExecutionResult result)
    {
        return InvokePrivateStatic<string?>("ParseMacOsDesktopVersion", result);
    }

    private static string? GetWindowsDesktopVersion(
        string? localAppData,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        return InvokePrivateStatic<string?>(
            "GetWindowsDesktopVersion",
            localAppData,
            directoryExists,
            enumerateDirectories);
    }

    private static T InvokePrivateStatic<T>(string name, params object?[] arguments)
    {
        var method = typeof(DesktopDeepLinkService).GetMethod(
            name,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        return (T)method.Invoke(null, arguments)!;
    }
}
