using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeDebugUtilitiesTests
{
    [Fact]
    public void RedactSecrets_Redacts_Short_And_Long_Secret_Fields()
    {
        var result = BridgeDebugUtilities.RedactSecrets(
            """{"token":"short","session_ingress_token":"1234567890abcdefghijklmnop","safe":"value"}""");

        Assert.Contains(@"""token"":""[REDACTED]""", result, StringComparison.Ordinal);
        Assert.Contains(@"""session_ingress_token"":""12345678...mnop""", result, StringComparison.Ordinal);
        Assert.Contains(@"""safe"":""value""", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSecrets_Redacts_EmbeddedSecretFields_WithoutRequiringValidJson()
    {
        const string payload =
            """[bridge:api] >>> {"token":"short","environment_secret":"1234567890abcdefghijklmnop"} trailing text""";

        var result = BridgeDebugUtilities.RedactSecrets(payload);

        Assert.Equal(
            """[bridge:api] >>> {"token":"[REDACTED]","environment_secret":"12345678...mnop"} trailing text""",
            result);
    }

    [Fact]
    public void RedactSecrets_PreservesOriginalWhitespace_WhenRedacting()
    {
        const string payload =
            """
            {
              "token" : "short",
              "safe" : "value"
            }
            """;

        var result = BridgeDebugUtilities.RedactSecrets(payload);

        Assert.Contains(@"""token"":""[REDACTED]""", result, StringComparison.Ordinal);
        Assert.Contains(@"""safe"" : ""value""", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugTruncate_Flattens_Newlines_And_Respects_Length_Limit()
    {
        var result = BridgeDebugUtilities.DebugTruncate("line1\nline2");

        Assert.Equal(@"line1\nline2", result);
    }

    [Fact]
    public void DebugBody_Redacts_And_Truncates_Json_Serializable_Input()
    {
        var payload = new
        {
            access_token = "1234567890abcdefghijklmnop",
            body = new string('a', 3000)
        };

        var result = BridgeDebugUtilities.DebugBody(payload);

        Assert.Contains("12345678...mnop", result, StringComparison.Ordinal);
        Assert.Contains("... (", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractErrorDetail_Reads_Message_And_Nested_Error_Message()
    {
        var message = BridgeDebugUtilities.ExtractErrorDetail(
            JsonNode.Parse("""{"message":"top-level"}"""));
        var nested = BridgeDebugUtilities.ExtractErrorDetail(
            JsonNode.Parse("""{"error":{"message":"nested"}}"""));

        Assert.Equal("top-level", message);
        Assert.Equal("nested", nested);
    }
}
