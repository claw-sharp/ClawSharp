// TS parity status: focused C# coverage for the metadata-backed assistant API-error helper used as the closest viable alternative to the TypeScript assistant message fields.
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public class QueryAssistantApiErrorMessageTests
{
    [Fact]
    public void CreateAssistantApiErrorMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(
            content: "Rate limited",
            apiError: "overloaded_error",
            error: "rate_limit",
            errorDetails: "retry later");

        Assert.Equal(MessageRole.Assistant, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal("Rate limited", block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("True", block.Metadata!["isApiErrorMessage"]);
        Assert.Equal("overloaded_error", block.Metadata["apiError"]);
        Assert.Equal("rate_limit", block.Metadata["error"]);
        Assert.Equal("retry later", block.Metadata["errorDetails"]);
    }

    [Fact]
    public void CreateAssistantApiErrorMessage_Uses_NoContent_Fallback_When_Content_Is_Empty()
    {
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(content: "");

        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(ChatMessageFactory.NoContentMessage, block.Value);
        Assert.Equal("True", block.Metadata!["isApiErrorMessage"]);
        Assert.False(block.Metadata.ContainsKey("apiError"));
        Assert.False(block.Metadata.ContainsKey("error"));
        Assert.False(block.Metadata.ContainsKey("errorDetails"));
    }
}
