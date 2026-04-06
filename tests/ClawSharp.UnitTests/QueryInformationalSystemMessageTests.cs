// TS origin: ./utils/messages.ts
// TS parity status: focused C# coverage for the metadata-backed informational system-message helper used as the closest viable alternative to the TypeScript structured system-message fields.
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public class QueryInformationalSystemMessageTests
{
    [Fact]
    public void CreateSystemMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateSystemMessage(
            content: "Hook blocked continuation",
            level: "warning",
            toolUseId: "tool-1",
            preventContinuation: true);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal("Hook blocked continuation", block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("informational", block.Metadata!["subtype"]);
        Assert.Equal("False", block.Metadata["isMeta"]);
        Assert.Equal("warning", block.Metadata["level"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("True", block.Metadata["preventContinuation"]);
    }

    [Fact]
    public void CreateSystemMessage_Omits_Optional_Metadata_When_Not_Provided()
    {
        var message = ChatMessageFactory.CreateSystemMessage(
            content: "Saved session",
            level: "info");

        var block = Assert.Single(message.ContentBlocks);
        Assert.NotNull(block.Metadata);
        Assert.Equal("informational", block.Metadata!["subtype"]);
        Assert.Equal("info", block.Metadata["level"]);
        Assert.False(block.Metadata.ContainsKey("toolUseID"));
        Assert.False(block.Metadata.ContainsKey("preventContinuation"));
    }
}
