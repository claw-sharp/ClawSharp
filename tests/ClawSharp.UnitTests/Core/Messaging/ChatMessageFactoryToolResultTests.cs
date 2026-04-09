using ClawSharp.Core;

public sealed class ChatMessageFactoryToolResultTests
{
    [Fact]
    public void CreateToolResult_Includes_Success_Metadata_When_Provided()
    {
        var message = ChatMessageFactory.CreateToolResult(
            "tool-use-1",
            "Glob",
            "Glob requires a non-empty pattern.",
            success: false);

        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.ToolResult, block.Kind);
        Assert.Equal("False", block.Metadata?["success"]);
    }
}
