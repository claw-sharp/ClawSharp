using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class InboundAttachmentUtilitiesTests
{
    [Fact]
    public void ExtractInboundAttachments_Returns_Empty_For_Missing_Or_Invalid_Shapes()
    {
        Assert.Empty(InboundAttachmentUtilities.ExtractInboundAttachments(null));
        Assert.Empty(InboundAttachmentUtilities.ExtractInboundAttachments(JsonNode.Parse("""{"foo":1}""")));
        Assert.Empty(InboundAttachmentUtilities.ExtractInboundAttachments(JsonNode.Parse("""{"file_attachments":[{"file_uuid":"u"}]}""")));
        Assert.Empty(InboundAttachmentUtilities.ExtractInboundAttachments(JsonNode.Parse("""{"file_attachments":[1]}""")));
    }

    [Fact]
    public void ExtractInboundAttachments_Returns_Parsed_Attachments()
    {
        var attachments = InboundAttachmentUtilities.ExtractInboundAttachments(
            JsonNode.Parse(
                """
                {
                  "file_attachments": [
                    { "file_uuid": "uuid-1", "file_name": "foo.txt" },
                    { "file_uuid": "uuid-2", "file_name": "dir/bar.png" }
                  ]
                }
                """));

        Assert.Collection(
            attachments,
            first =>
            {
                Assert.Equal("uuid-1", first.FileUuid);
                Assert.Equal("foo.txt", first.FileName);
            },
            second =>
            {
                Assert.Equal("uuid-2", second.FileUuid);
                Assert.Equal("dir/bar.png", second.FileName);
            });
    }

    [Theory]
    [InlineData("foo.txt", "foo.txt")]
    [InlineData("../dir/evil name?.txt", "evil_name_.txt")]
    [InlineData("////", "attachment")]
    public void SanitizeFileName_Follows_Ts_Basename_And_Replacement_Rules(string input, string expected)
    {
        Assert.Equal(expected, InboundAttachmentUtilities.SanitizeFileName(input));
    }

    [Fact]
    public async Task ResolveInboundAttachmentsAsync_Joins_Quoted_Path_Refs_And_Skips_Undefined()
    {
        List<string> debug = [];
        var prefix = await InboundAttachmentUtilities.ResolveInboundAttachmentsAsync(
            [
                new InboundAttachment("uuid-1", "a.txt"),
                new InboundAttachment("uuid-2", "b.txt")
            ],
            attachment => Task.FromResult<string?>(attachment.FileUuid == "uuid-1" ? @"C:\Users\John Smith\a.txt" : null),
            debug.Add);

        Assert.Equal("@\"C:\\Users\\John Smith\\a.txt\" ", prefix);
        Assert.Contains("resolving 2 attachment(s)", debug);
    }

    [Fact]
    public void PrependPathRefs_String_Prepends_Prefix_And_Noops_On_Empty_Prefix()
    {
        Assert.Equal("prefix body", InboundAttachmentUtilities.PrependPathRefs("body", "prefix "));
        Assert.Equal("body", InboundAttachmentUtilities.PrependPathRefs("body", ""));
    }

    [Fact]
    public void PrependPathRefs_Array_Targets_Last_Text_Block_And_Preserves_Non_Text_Blocks()
    {
        var content = JsonNode.Parse(
            """
            [
              { "type": "text", "text": "first" },
              { "type": "image", "source": "x" },
              { "type": "text", "text": "last" }
            ]
            """)!.AsArray();

        var updated = InboundAttachmentUtilities.PrependPathRefs(content, "@\"/tmp/f.txt\" ");

        Assert.Equal("first", updated[0]!["text"]!.GetValue<string>());
        Assert.Equal("image", updated[1]!["type"]!.GetValue<string>());
        Assert.Equal("@\"/tmp/f.txt\" last", updated[2]!["text"]!.GetValue<string>());
        Assert.Equal("last", content[2]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void PrependPathRefs_Array_Appends_Text_Block_When_None_Exists()
    {
        var content = JsonNode.Parse("""[{ "type": "image", "source": "x" }]""")!.AsArray();

        var updated = InboundAttachmentUtilities.PrependPathRefs(content, "@\"/tmp/f.txt\" ");

        Assert.Equal(2, updated.Count);
        Assert.Equal("text", updated[1]!["type"]!.GetValue<string>());
        Assert.Equal("@\"/tmp/f.txt\"", updated[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ResolveAndPrependAsync_Fast_Path_Returns_Same_Value_When_No_Attachments()
    {
        var content = "hello";
        var resolved = await InboundAttachmentUtilities.ResolveAndPrependAsync(
            JsonNode.Parse("""{"foo":1}"""),
            content,
            _ => throw new InvalidOperationException("should not execute"));

        Assert.Same(content, resolved);
    }

    [Fact]
    public async Task ResolveAndPrependAsync_Array_Uses_Extract_Resolve_And_Prepend_Pipeline()
    {
        var message = JsonNode.Parse("""{"file_attachments":[{"file_uuid":"uuid-1","file_name":"foo.txt"}]}""");
        var content = JsonNode.Parse("""[{ "type": "image", "source": "x" }, { "type": "text", "text": "body" }]""")!.AsArray();

        var resolved = await InboundAttachmentUtilities.ResolveAndPrependAsync(
            message,
            content,
            attachments =>
            {
                Assert.Single(attachments);
                Assert.Equal("uuid-1", attachments[0].FileUuid);
                return Task.FromResult("@\"/tmp/foo.txt\" ");
            });

        Assert.Equal("@\"/tmp/foo.txt\" body", resolved[1]!["text"]!.GetValue<string>());
    }
}
