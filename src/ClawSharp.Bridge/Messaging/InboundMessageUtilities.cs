using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record BridgeSdkMessagePayload(
    JsonNode? Content);

public sealed record BridgeSdkMessage(
    string Type,
    BridgeSdkMessagePayload? Message,
    string? Uuid = null);

public sealed record BridgeInboundMessageFields(
    JsonNode Content,
    string? Uuid);

public static class InboundMessageUtilities
{
    public static BridgeInboundMessageFields? ExtractInboundMessageFields(BridgeSdkMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.Equals(message.Type, "user", StringComparison.Ordinal))
        {
            return null;
        }

        var content = message.Message?.Content;
        if (content is null)
        {
            return null;
        }

        if (content is JsonArray emptyArray && emptyArray.Count == 0)
        {
            return null;
        }

        return new BridgeInboundMessageFields(
            content is JsonArray blocks ? NormalizeImageBlocks(blocks) : content,
            message.Uuid);
    }

    public static JsonArray NormalizeImageBlocks(JsonArray blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (!blocks.Any(IsMalformedBase64Image))
        {
            return blocks;
        }

        JsonArray normalized = [];
        foreach (var blockNode in blocks)
        {
            if (blockNode is not JsonObject block || !IsMalformedBase64Image(block))
            {
                normalized.Add(blockNode?.DeepClone());
                continue;
            }

            var source = block["source"]?.AsObject();
            var base64Data = source?["data"]?.GetValue<string>() ?? string.Empty;
            var mediaType = source?["mediaType"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mediaType))
            {
                mediaType = DetectImageFormatFromBase64(base64Data);
            }

            normalized.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = mediaType,
                    ["data"] = base64Data
                }
            });
        }

        return normalized;
    }

    private static bool IsMalformedBase64Image(JsonNode? blockNode)
    {
        if (blockNode is not JsonObject block)
        {
            return false;
        }

        if (!string.Equals(block["type"]?.GetValue<string>(), "image", StringComparison.Ordinal))
        {
            return false;
        }

        var source = block["source"] as JsonObject;
        if (!string.Equals(source?["type"]?.GetValue<string>(), "base64", StringComparison.Ordinal))
        {
            return false;
        }

        return source?["media_type"] is null;
    }

    private static string DetectImageFormatFromBase64(string base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return "image/png";
        }

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(base64);
        }
        catch
        {
            return "image/png";
        }

        if (buffer.Length < 4)
        {
            return "image/png";
        }

        if (buffer[0] == 0x89 &&
            buffer[1] == 0x50 &&
            buffer[2] == 0x4E &&
            buffer[3] == 0x47)
        {
            return "image/png";
        }

        if (buffer[0] == 0xFF &&
            buffer[1] == 0xD8 &&
            buffer[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (buffer[0] == 0x47 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46)
        {
            return "image/gif";
        }

        if (buffer.Length >= 12 &&
            buffer[0] == 0x52 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46 &&
            buffer[3] == 0x46 &&
            buffer[8] == 0x57 &&
            buffer[9] == 0x45 &&
            buffer[10] == 0x42 &&
            buffer[11] == 0x50)
        {
            return "image/webp";
        }

        return "image/png";
    }
}
