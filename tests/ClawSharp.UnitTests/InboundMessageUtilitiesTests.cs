// TS origin: ./bridge/inboundMessages.ts
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class InboundMessageUtilitiesTests
{
    [Fact]
    public void ExtractInboundMessageFields_Skips_NonUser_And_Empty_Content()
    {
        Assert.Null(InboundMessageUtilities.ExtractInboundMessageFields(
            new BridgeSdkMessage("assistant", new BridgeSdkMessagePayload("hello"))));

        Assert.Null(InboundMessageUtilities.ExtractInboundMessageFields(
            new BridgeSdkMessage("user", new BridgeSdkMessagePayload(null))));

        Assert.Null(InboundMessageUtilities.ExtractInboundMessageFields(
            new BridgeSdkMessage("user", new BridgeSdkMessagePayload(new JsonArray()))));
    }

    [Fact]
    public void ExtractInboundMessageFields_Preserves_String_Content_And_Uuid()
    {
        var result = InboundMessageUtilities.ExtractInboundMessageFields(
            new BridgeSdkMessage("user", new BridgeSdkMessagePayload("hello"), "uuid-1"));

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Content.GetValue<string>());
        Assert.Equal("uuid-1", result.Uuid);
    }

    [Fact]
    public void NormalizeImageBlocks_Returns_Original_Array_When_No_Normalization_Is_Needed()
    {
        JsonArray blocks =
        [
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = "image/png",
                    ["data"] = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47])
                }
            }
        ];

        var normalized = InboundMessageUtilities.NormalizeImageBlocks(blocks);

        Assert.Same(blocks, normalized);
    }

    [Fact]
    public void NormalizeImageBlocks_Uses_CamelCase_MediaType_And_Falls_Back_To_Base64_Detection()
    {
        var jpegBase64 = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0x00]);
        JsonArray blocks =
        [
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["mediaType"] = "image/webp",
                    ["data"] = jpegBase64
                }
            },
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["data"] = jpegBase64
                }
            }
        ];

        var normalized = InboundMessageUtilities.NormalizeImageBlocks(blocks);

        Assert.NotSame(blocks, normalized);
        Assert.Equal("image/webp", normalized[0]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal("image/jpeg", normalized[1]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(jpegBase64, normalized[1]!["source"]!["data"]!.GetValue<string>());
    }

    [Fact]
    public void ExtractInboundMessageFields_Normalizes_Image_Block_Arrays()
    {
        var pngBase64 = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]);
        JsonArray content =
        [
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["data"] = pngBase64
                }
            }
        ];

        var result = InboundMessageUtilities.ExtractInboundMessageFields(
            new BridgeSdkMessage("user", new BridgeSdkMessagePayload(content), "uuid-2"));

        Assert.NotNull(result);
        Assert.IsType<JsonArray>(result!.Content);
        Assert.Equal("image/png", result.Content[0]!["source"]!["media_type"]!.GetValue<string>());
    }
}
