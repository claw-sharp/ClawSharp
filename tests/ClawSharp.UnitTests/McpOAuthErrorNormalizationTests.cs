// TS origin: ./services/mcp/auth.ts
using System.Text.Json;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpOAuthErrorNormalizationTests
{
    [Theory]
    [InlineData("invalid_refresh_token")]
    [InlineData("expired_refresh_token")]
    [InlineData("token_expired")]
    public void NormalizeSuccessResponseBody_RewritesKnownAliasesToInvalidGrant(string errorCode)
    {
        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(
            200,
            $$"""{"error":"{{errorCode}}"}""");

        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            $"Server returned non-standard error code: {errorCode}",
            document.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public void NormalizeSuccessResponseBody_PreservesExplicitErrorDescription()
    {
        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(
            200,
            """{"error":"invalid_refresh_token","error_description":"refresh expired"}""");

        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("refresh expired", document.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public void NormalizeSuccessResponseBody_PreservesOAuthTokenPayloads()
    {
        const string body = """{"access_token":"token","refresh_token":"refresh","expires_in":3600}""";

        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(200, body);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(body, response.Body);
    }

    [Fact]
    public void NormalizeSuccessResponseBody_PreservesNonAliasErrors()
    {
        const string body = """{"error":"invalid_client","error_description":"bad client"}""";

        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(200, body);

        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("invalid_client", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("bad client", document.RootElement.GetProperty("error_description").GetString());
    }

    [Fact]
    public void NormalizeSuccessResponseBody_PreservesNonJsonBodies()
    {
        const string body = "plain text";

        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(200, body);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(body, response.Body);
    }

    [Fact]
    public void NormalizeSuccessResponseBody_PreservesNonSuccessStatusCodes()
    {
        const string body = """{"error":"invalid_refresh_token"}""";

        var response = McpOAuthErrorNormalization.NormalizeSuccessResponseBody(401, body);

        Assert.Equal(401, response.StatusCode);
        Assert.Equal(body, response.Body);
    }
}
