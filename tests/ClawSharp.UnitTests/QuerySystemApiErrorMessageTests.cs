// TS parity status: focused C# coverage for the metadata-backed system api_error helper used as the closest viable alternative to the TypeScript structured system-message object fields.
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public class QuerySystemApiErrorMessageTests
{
    [Fact]
    public void CreateSystemApiErrorMessage_Stores_Ts_Shaped_Metadata()
    {
        var error = new JsonObject
        {
            ["name"] = "APIConnectionError",
            ["message"] = "connection reset"
        };
        var cause = new JsonObject
        {
            ["name"] = "IOException",
            ["message"] = "socket closed"
        };

        var message = ChatMessageFactory.CreateSystemApiErrorMessage(
            error,
            retryInMs: 1500,
            retryAttempt: 2,
            maxRetries: 5,
            cause);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal("Model API Error: connection reset Retrying in 1s (Attempt 2 of 5)...", block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("api_error", block.Metadata!["subtype"]);
        Assert.Equal("error", block.Metadata["level"]);
        Assert.Equal("1500", block.Metadata["retryInMs"]);
        Assert.Equal("2", block.Metadata["retryAttempt"]);
        Assert.Equal("5", block.Metadata["maxRetries"]);
        Assert.Equal(error.ToJsonString(), block.Metadata["error"]);
        Assert.Equal(cause.ToJsonString(), block.Metadata["cause"]);
    }

    [Fact]
    public void CreateSystemApiErrorMessage_Omits_Cause_When_Not_Provided()
    {
        var error = new JsonObject
        {
            ["name"] = "RateLimitError",
            ["message"] = "too many requests"
        };

        var message = ChatMessageFactory.CreateSystemApiErrorMessage(
            error,
            retryInMs: 1000,
            retryAttempt: 1,
            maxRetries: 3);

        var block = Assert.Single(message.ContentBlocks);
        Assert.NotNull(block.Metadata);
        Assert.Equal(error.ToJsonString(), block.Metadata!["error"]);
        Assert.False(block.Metadata.ContainsKey("cause"));
    }
}
