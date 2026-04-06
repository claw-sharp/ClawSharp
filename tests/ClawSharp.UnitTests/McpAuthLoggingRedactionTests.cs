// TS origin: ./services/mcp/auth.ts
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpAuthLoggingRedactionTests
{
    [Fact]
    public void RedactSensitiveUrlParams_RedactsConfiguredOAuthParameters()
    {
        var url = "https://example.test/callback?state=abc&nonce=def&code=ghi&scope=read";

        var result = McpAuthLoggingRedaction.RedactSensitiveUrlParams(url);

        Assert.Equal(
            "https://example.test/callback?state=%5BREDACTED%5D&nonce=%5BREDACTED%5D&code=%5BREDACTED%5D&scope=read",
            result);
    }

    [Fact]
    public void RedactSensitiveUrlParams_PreservesNonSensitiveParameters()
    {
        var url = "https://example.test/callback?scope=read&prompt=consent";

        var result = McpAuthLoggingRedaction.RedactSensitiveUrlParams(url);

        Assert.Equal("https://example.test/callback?scope=read&prompt=consent", result);
    }

    [Fact]
    public void RedactSensitiveUrlParams_RedactsEncodedSensitiveParameterNames()
    {
        var url = "https://example.test/callback?code_verifier=abc&code_challenge_method=S256&code_challenge=xyz";

        var result = McpAuthLoggingRedaction.RedactSensitiveUrlParams(url);

        Assert.Equal(
            "https://example.test/callback?code_verifier=%5BREDACTED%5D&code_challenge_method=S256&code_challenge=%5BREDACTED%5D",
            result);
    }

    [Fact]
    public void RedactSensitiveUrlParams_ReturnsInputWhenUrlIsInvalid()
    {
        const string url = "not a valid url?state=abc&code=def";

        var result = McpAuthLoggingRedaction.RedactSensitiveUrlParams(url);

        Assert.Equal(url, result);
    }
}
