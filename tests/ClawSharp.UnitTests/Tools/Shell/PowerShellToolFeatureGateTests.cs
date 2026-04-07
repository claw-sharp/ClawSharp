// TS parity status: locks the current C# port of the PowerShell tool visibility gate for the existing tool surface; other TS feature-gated tools remain outside this milestone because their tool/runtime implementations are still absent.
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class PowerShellToolFeatureGateTests
{
    private static readonly SemaphoreSlim PowerShellToolEnvironmentLock = new(1, 1);

    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(true, "ant", null, true)]
    [InlineData(true, "ant", "0", false)]
    [InlineData(true, "ant", "false", false)]
    [InlineData(true, "external", null, false)]
    [InlineData(true, null, null, false)]
    [InlineData(true, "external", "1", true)]
    [InlineData(true, null, "true", true)]
    public void IsEnabled_Matches_Ts_PowerShell_Gate(
        bool isWindows,
        string? userType,
        string? configuredValue,
        bool expected)
    {
        Assert.Equal(expected, PowerShellToolFeatureGate.IsEnabled(userType, configuredValue, isWindows));
    }

    [Fact]
    public async Task ToolRegistry_Only_Publishes_PowerShell_When_Gate_Is_Enabled()
    {
        await PowerShellToolEnvironmentLock.WaitAsync();
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        var originalGate = Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL");

        try
        {
            Environment.SetEnvironmentVariable("USER_TYPE", "external");
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL", null);

            var disabledRegistry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
            Assert.DoesNotContain(disabledRegistry.All, static tool => tool.Name == "PowerShell");

            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL", "true");

            var enabledRegistry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains(enabledRegistry.All, static tool => tool.Name == "PowerShell");
            }
            else
            {
                Assert.DoesNotContain(enabledRegistry.All, static tool => tool.Name == "PowerShell");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL", originalGate);
            PowerShellDetection.ResetPowerShellCache();
            PowerShellToolEnvironmentLock.Release();
        }
    }
}
