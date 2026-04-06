// TS origin: ./utils/messages.ts
// TS parity status: focused C# coverage for metadata-backed compact-boundary messages and pure compact-boundary detection helpers; live compaction execution is still intentionally unported.
using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryCompactBoundaryContractTests
{
    [Fact]
    public void CreateCompactBoundaryMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateCompactBoundaryMessage(
            trigger: "auto",
            preTokens: 12345,
            lastPreCompactMessageUuid: "msg-1",
            userContext: "ctx",
            messagesSummarized: 7);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal("Conversation compacted", block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("compact_boundary", block.Metadata!["subtype"]);
        Assert.Equal("info", block.Metadata["level"]);
        Assert.Equal("False", block.Metadata["isMeta"]);
        Assert.Equal("msg-1", block.Metadata["logicalParentUuid"]);

        var metadata = JsonSerializer.Deserialize<CompactBoundaryMetadata>(block.Metadata["compactMetadata"]);
        Assert.NotNull(metadata);
        Assert.Equal("auto", metadata!.Trigger);
        Assert.Equal(12345, metadata.PreTokens);
        Assert.Equal("ctx", metadata.UserContext);
        Assert.Equal(7, metadata.MessagesSummarized);
    }

    [Fact]
    public void CreateMicrocompactBoundaryMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateMicrocompactBoundaryMessage(
            trigger: "auto",
            preTokens: 9000,
            tokensSaved: 1200,
            compactedToolIds: ["tool-1", "tool-2"],
            clearedAttachmentUuids: ["att-1"]);

        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal("Context microcompacted", block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("microcompact_boundary", block.Metadata!["subtype"]);
        Assert.Equal("info", block.Metadata["level"]);
        Assert.Equal("False", block.Metadata["isMeta"]);

        var metadata = JsonSerializer.Deserialize<MicrocompactBoundaryMetadata>(block.Metadata["microcompactMetadata"]);
        Assert.NotNull(metadata);
        Assert.Equal(9000, metadata!.PreTokens);
        Assert.Equal(1200, metadata.TokensSaved);
        Assert.Equal(["tool-1", "tool-2"], metadata.CompactedToolIds);
        Assert.Equal(["att-1"], metadata.ClearedAttachmentUuids);
    }

    [Fact]
    public void CompactBoundaryHelpers_Find_And_Slice_From_Last_Boundary()
    {
        var before = ChatMessageFactory.CreateText(MessageRole.User, "before");
        var firstBoundary = ChatMessageFactory.CreateCompactBoundaryMessage("manual", 100);
        var middle = ChatMessageFactory.CreateText(MessageRole.Assistant, "middle");
        var lastBoundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 200);
        var after = ChatMessageFactory.CreateText(MessageRole.Assistant, "after");
        var messages = new[] { before, firstBoundary, middle, lastBoundary, after };

        Assert.True(QueryCompactBoundaryHelpers.IsCompactBoundaryMessage(lastBoundary));
        Assert.Equal(3, QueryCompactBoundaryHelpers.FindLastCompactBoundaryIndex(messages));
        Assert.Equal(new[] { lastBoundary.Id, after.Id }, QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(messages).Select(m => m.Id).ToArray());
    }
}
