// TS origin: ./bridge/workSecret.ts
using System.Text;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeWorkSecretUtilitiesTests
{
    [Fact]
    public void DecodeWorkSecret_Parses_And_Validates_Work_Secret()
    {
        var secret = ToBase64Url(
            """
            {"version":1,"session_ingress_token":"sk-ant-si-token","api_base_url":"https://api.example.com","sources":[{"type":"git","gitInfo":{"type":"git","repo":"https://example.com/repo.git","ref":"main","token":"abc"}}],"auth":[{"type":"oauth","token":"secret"}],"claude_code_args":{"foo":"bar"},"environment_variables":{"A":"B"},"use_code_sessions":true}
            """);

        var decoded = BridgeWorkSecretUtilities.DecodeWorkSecret(secret);

        Assert.Equal(1, decoded.Version);
        Assert.Equal("sk-ant-si-token", decoded.SessionIngressToken);
        Assert.Equal("https://api.example.com", decoded.ApiBaseUrl);
        Assert.Single(decoded.Sources);
        Assert.Equal("git", decoded.Sources[0].Type);
        Assert.NotNull(decoded.Sources[0].GitInfo);
        Assert.Equal("https://example.com/repo.git", decoded.Sources[0].GitInfo!.Repo);
        Assert.Single(decoded.Auth);
        Assert.Equal("secret", decoded.Auth[0].Token);
        Assert.Equal("bar", decoded.ClaudeCodeArgs!["foo"]);
        Assert.Equal("B", decoded.EnvironmentVariables!["A"]);
        Assert.True(decoded.UseCodeSessions);
    }

    [Fact]
    public void DecodeWorkSecret_Rejects_Unsupported_Version_And_Missing_Fields()
    {
        var unsupported = ToBase64Url("""{"version":2,"session_ingress_token":"token","api_base_url":"https://api.example.com","sources":[],"auth":[]}""");
        var missingToken = ToBase64Url("""{"version":1,"session_ingress_token":"","api_base_url":"https://api.example.com","sources":[],"auth":[]}""");
        var missingApiBase = ToBase64Url("""{"version":1,"session_ingress_token":"token","sources":[],"auth":[]}""");

        Assert.Equal(
            "Unsupported work secret version: 2",
            Assert.Throws<InvalidOperationException>(() => BridgeWorkSecretUtilities.DecodeWorkSecret(unsupported)).Message);
        Assert.Equal(
            "Invalid work secret: missing or empty session_ingress_token",
            Assert.Throws<InvalidOperationException>(() => BridgeWorkSecretUtilities.DecodeWorkSecret(missingToken)).Message);
        Assert.Equal(
            "Invalid work secret: missing api_base_url",
            Assert.Throws<InvalidOperationException>(() => BridgeWorkSecretUtilities.DecodeWorkSecret(missingApiBase)).Message);
    }

    [Theory]
    [InlineData("https://api.example.com", "session_123", "wss://api.example.com/v1/session_ingress/ws/session_123")]
    [InlineData("http://localhost:4000/", "cse_123", "ws://localhost:4000/v2/session_ingress/ws/cse_123")]
    [InlineData("http://127.0.0.1:5000///", "cse_456", "ws://127.0.0.1:5000/v2/session_ingress/ws/cse_456")]
    public void BuildSdkUrl_Uses_Expected_Protocol_And_Path(string apiBaseUrl, string sessionId, string expected)
    {
        Assert.Equal(expected, BridgeWorkSecretUtilities.BuildSdkUrl(apiBaseUrl, sessionId));
    }

    [Theory]
    [InlineData("cse_1234abcd", "session_1234abcd", true)]
    [InlineData("cse_staging_1234abcd", "session_1234abcd", true)]
    [InlineData("one", "two", false)]
    [InlineData("cse_abc", "session_abc", false)]
    public void SameSessionId_Matches_By_Tagless_Body(string a, string b, bool expected)
    {
        Assert.Equal(expected, BridgeWorkSecretUtilities.SameSessionId(a, b));
    }

    [Fact]
    public void BuildCcrV2SdkUrl_Trims_Trailing_Slashes()
    {
        var url = BridgeWorkSecretUtilities.BuildCcrV2SdkUrl("https://api.example.com///", "session_123");

        Assert.Equal("https://api.example.com/v1/code/sessions/session_123", url);
    }

    private static string ToBase64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
