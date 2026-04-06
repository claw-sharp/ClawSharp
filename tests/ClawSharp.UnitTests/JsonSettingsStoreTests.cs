// TS origin: no direct 1:1 TS source; ClawSharp-specific C# unit coverage for settings behavior derived from ./utils/config.ts.
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public class JsonSettingsStoreTests
{
    [Fact]
    public async Task Save_Then_Load_RoundTrips_Settings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var store = new JsonSettingsStore(settingsPath);
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "test-model",
                PermissionMode = PermissionMode.Plan,
                EnableTelemetry = true
            },
            Terminal = new TerminalSettings
            {
                ShowTimestamps = true,
                UseColor = false
            },
            AgentModels = new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal)
            {
                ["gpt-4o"] = new()
                {
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKey = "sk-openai",
                    Provider = "openai",
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["x-test-header"] = "value"
                    }
                }
            },
            AgentRouting = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["default"] = "gpt-4o"
            }
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal("test-model", loaded.Runtime.Model);
        Assert.Equal(PermissionMode.Plan, loaded.Runtime.PermissionMode);
        Assert.True(loaded.Runtime.EnableTelemetry);
        Assert.True(loaded.Terminal.ShowTimestamps);
        Assert.False(loaded.Terminal.UseColor);
        Assert.Equal("https://api.openai.com/v1", loaded.AgentModels["gpt-4o"].BaseUrl);
        Assert.Equal("openai", loaded.AgentModels["gpt-4o"].Provider);
        Assert.Equal("value", loaded.AgentModels["gpt-4o"].Headers["x-test-header"]);
        Assert.Equal("gpt-4o", loaded.AgentRouting["default"]);
    }
}
