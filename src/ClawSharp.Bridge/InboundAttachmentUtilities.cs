// TS origin: ./bridge/inboundAttachments.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record InboundAttachment(
    string FileUuid,
    string FileName);

public static class InboundAttachmentUtilities
{
    public static IReadOnlyList<InboundAttachment> ExtractInboundAttachments(JsonNode? message)
    {
        if (message is not JsonObject obj ||
            obj["file_attachments"] is not JsonArray attachmentsArray)
        {
            return Array.Empty<InboundAttachment>();
        }

        List<InboundAttachment> attachments = [];
        foreach (var item in attachmentsArray)
        {
            if (item is not JsonObject attachmentObject)
            {
                return Array.Empty<InboundAttachment>();
            }

            var fileUuid = attachmentObject["file_uuid"]?.GetValue<string>();
            var fileName = attachmentObject["file_name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(fileUuid) || string.IsNullOrEmpty(fileName))
            {
                return Array.Empty<InboundAttachment>();
            }

            attachments.Add(new InboundAttachment(fileUuid, fileName));
        }

        return attachments;
    }

    public static string SanitizeFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var baseName = Path.GetFileName(name);
        Span<char> buffer = stackalloc char[baseName.Length];
        var index = 0;
        foreach (var character in baseName)
        {
            buffer[index++] = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_';
        }

        var sanitized = new string(buffer[..index]);
        return string.IsNullOrEmpty(sanitized) ? "attachment" : sanitized;
    }

    public static async Task<string> ResolveInboundAttachmentsAsync(
        IReadOnlyList<InboundAttachment> attachments,
        Func<InboundAttachment, Task<string?>> resolveOneAsync,
        Action<string>? onDebug = null)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(resolveOneAsync);

        if (attachments.Count == 0)
        {
            return string.Empty;
        }

        onDebug?.Invoke($"resolving {attachments.Count} attachment(s)");
        var paths = await Task.WhenAll(attachments.Select(resolveOneAsync));
        var ok = paths.Where(static path => path is not null).Cast<string>().ToArray();
        if (ok.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(" ", ok.Select(static path => $"@\"{path}\"")) + " ";
    }

    public static string PrependPathRefs(string content, string prefix)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(prefix);

        return string.IsNullOrEmpty(prefix) ? content : prefix + content;
    }

    public static JsonArray PrependPathRefs(JsonArray content, string prefix)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(prefix);

        if (string.IsNullOrEmpty(prefix))
        {
            return content.DeepClone().AsArray();
        }

        var clone = content.DeepClone().AsArray();
        var lastTextIndex = -1;
        for (var i = 0; i < clone.Count; i++)
        {
            if (clone[i] is JsonObject block &&
                string.Equals(block["type"]?.GetValue<string>(), "text", StringComparison.Ordinal))
            {
                lastTextIndex = i;
            }
        }

        if (lastTextIndex >= 0 &&
            clone[lastTextIndex] is JsonObject textBlock)
        {
            var text = textBlock["text"]?.GetValue<string>() ?? string.Empty;
            textBlock["text"] = prefix + text;
            return clone;
        }

        clone.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = prefix.TrimEnd()
        });
        return clone;
    }

    public static async Task<string> ResolveAndPrependAsync(
        JsonNode? message,
        string content,
        Func<IReadOnlyList<InboundAttachment>, Task<string>> resolveInboundAttachmentsAsync)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resolveInboundAttachmentsAsync);

        var attachments = ExtractInboundAttachments(message);
        if (attachments.Count == 0)
        {
            return content;
        }

        var prefix = await resolveInboundAttachmentsAsync(attachments);
        return PrependPathRefs(content, prefix);
    }

    public static async Task<JsonArray> ResolveAndPrependAsync(
        JsonNode? message,
        JsonArray content,
        Func<IReadOnlyList<InboundAttachment>, Task<string>> resolveInboundAttachmentsAsync)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resolveInboundAttachmentsAsync);

        var attachments = ExtractInboundAttachments(message);
        if (attachments.Count == 0)
        {
            return content;
        }

        var prefix = await resolveInboundAttachmentsAsync(attachments);
        return PrependPathRefs(content, prefix);
    }
}
