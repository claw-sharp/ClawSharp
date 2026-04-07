using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeConfigUtilitiesTests
{
    [Fact]
    public void Override_Helpers_Only_Apply_For_Ant_Users()
    {
        IReadOnlyDictionary<string, string?> antEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "ant",
            ["CLAUDE_BRIDGE_OAUTH_TOKEN"] = "override-token",
            ["CLAUDE_BRIDGE_BASE_URL"] = "https://staging.example.com"
        };
        IReadOnlyDictionary<string, string?> normalEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "external",
            ["CLAUDE_BRIDGE_OAUTH_TOKEN"] = "override-token",
            ["CLAUDE_BRIDGE_BASE_URL"] = "https://staging.example.com"
        };

        Assert.Equal("override-token", BridgeConfigUtilities.GetBridgeTokenOverride(antEnvironment));
        Assert.Equal("https://staging.example.com", BridgeConfigUtilities.GetBridgeBaseUrlOverride(antEnvironment));
        Assert.Null(BridgeConfigUtilities.GetBridgeTokenOverride(normalEnvironment));
        Assert.Null(BridgeConfigUtilities.GetBridgeBaseUrlOverride(normalEnvironment));
    }

    [Fact]
    public void GetBridgeAccessToken_Prefers_Override_Then_Falls_Back_To_Injected_OAuth_Source()
    {
        IReadOnlyDictionary<string, string?> antEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "ant",
            ["CLAUDE_BRIDGE_OAUTH_TOKEN"] = "override-token"
        };
        IReadOnlyDictionary<string, string?> externalEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "external"
        };

        var dependencies = new BridgeConfigDependencies(
            GetClaudeAiAccessToken: () => "oauth-token");

        Assert.Equal("override-token", BridgeConfigUtilities.GetBridgeAccessToken(antEnvironment, dependencies));
        Assert.Equal("oauth-token", BridgeConfigUtilities.GetBridgeAccessToken(externalEnvironment, dependencies));
    }

    [Fact]
    public void GetBridgeBaseUrl_Prefers_Override_Then_Falls_Back_To_Injected_Config()
    {
        IReadOnlyDictionary<string, string?> antEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "ant",
            ["CLAUDE_BRIDGE_BASE_URL"] = "https://staging.example.com"
        };
        IReadOnlyDictionary<string, string?> externalEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["USER_TYPE"] = "external"
        };

        var dependencies = new BridgeConfigDependencies(
            GetBaseApiUrl: () => "https://api.example.com");

        Assert.Equal("https://staging.example.com", BridgeConfigUtilities.GetBridgeBaseUrl(antEnvironment, dependencies));
        Assert.Equal("https://api.example.com", BridgeConfigUtilities.GetBridgeBaseUrl(externalEnvironment, dependencies));
    }

    [Fact]
    public void GetBridgeBaseUrl_Throws_When_No_Fallback_Config_Exists()
    {
        IReadOnlyDictionary<string, string?> environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() =>
            BridgeConfigUtilities.GetBridgeBaseUrl(environment, new BridgeConfigDependencies()));

        Assert.Equal("Base API URL is required.", error.Message);
    }
}
