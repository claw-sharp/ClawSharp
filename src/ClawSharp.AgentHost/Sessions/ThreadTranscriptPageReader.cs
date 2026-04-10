using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.AgentHost.Contracts;

namespace ClawSharp.AgentHost.Sessions;

internal sealed class ThreadTranscriptPageReader
{
    public async Task<ThreadMessagePage> ReadPageAsync(
        string transcriptPath,
        string threadId,
        string? beforeMessageId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(transcriptPath))
        {
            return new ThreadMessagePage([], false, null);
        }

        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var collected = new List<ThreadMessageDto>(normalizedPageSize + 1);
        var anchorReached = string.IsNullOrWhiteSpace(beforeMessageId);

        await foreach (var line in ReadLinesReverseAsync(transcriptPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = TryParseVisibleMessage(line, threadId);
            if (message is null)
            {
                continue;
            }

            if (!anchorReached)
            {
                if (string.Equals(message.Id, beforeMessageId, StringComparison.Ordinal))
                {
                    anchorReached = true;
                }

                continue;
            }

            collected.Add(message);
            if (collected.Count > normalizedPageSize)
            {
                break;
            }
        }

        var hasMoreMessages = collected.Count > normalizedPageSize;
        if (hasMoreMessages)
        {
            collected.RemoveAt(collected.Count - 1);
        }

        collected.Reverse();
        var nextBeforeMessageId = hasMoreMessages ? collected.FirstOrDefault()?.Id : null;
        return new ThreadMessagePage(collected, hasMoreMessages, nextBeforeMessageId);
    }

    private static ThreadMessageDto? TryParseVisibleMessage(string line, string threadId)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch
        {
            return null;
        }

        if (root is null)
        {
            return null;
        }

        var type = root["type"]?.GetValue<string>();
        if (type is not ("user" or "assistant" or "system" or "attachment"))
        {
            return null;
        }

        var id = root["uuid"]?.GetValue<string>();
        var messageObject = root["message"] as JsonObject;
        var role = messageObject?["role"]?.GetValue<string>();
        var contentArray = messageObject?["content"] as JsonArray;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(role) || contentArray is null)
        {
            return null;
        }

        var contentParts = new List<string>(contentArray.Count);
        foreach (var item in contentArray)
        {
            if (item is not JsonObject block)
            {
                continue;
            }

            var blockType = block["type"]?.GetValue<string>();
            if (blockType == "text")
            {
                var text = block["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    contentParts.Add(text);
                }
            }
        }

        if (contentParts.Count == 0)
        {
            return null;
        }

        return new ThreadMessageDto(
            id,
            threadId,
            role.ToLowerInvariant(),
            string.Join(Environment.NewLine, contentParts),
            ParseTimestamp(root["timestamp"]));
    }

    private static DateTimeOffset ParseTimestamp(JsonNode? node)
    {
        return node is null
            ? DateTimeOffset.UtcNow
            : node.Deserialize<DateTimeOffset>()!;
    }

    private static async IAsyncEnumerable<string> ReadLinesReverseAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        var lineBytesReversed = new List<byte>(bufferSize);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize,
            useAsync: true);

        var position = stream.Length;
        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesToRead = (int)Math.Min(buffer.Length, position);
            position -= bytesToRead;
            stream.Position = position;
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

            for (var index = bytesRead - 1; index >= 0; index--)
            {
                var value = buffer[index];
                if (value == (byte)'\n')
                {
                    if (lineBytesReversed.Count == 0)
                    {
                        continue;
                    }

                    yield return DecodeReversedLine(lineBytesReversed);
                    lineBytesReversed.Clear();
                    continue;
                }

                lineBytesReversed.Add(value);
            }
        }

        if (lineBytesReversed.Count > 0)
        {
            yield return DecodeReversedLine(lineBytesReversed);
        }
    }

    private static string DecodeReversedLine(List<byte> lineBytesReversed)
    {
        lineBytesReversed.Reverse();
        var line = Encoding.UTF8.GetString(lineBytesReversed.ToArray());
        return line.TrimEnd('\r');
    }
}

internal sealed record ThreadMessagePage(
    IReadOnlyList<ThreadMessageDto> Messages,
    bool HasMoreMessages,
    string? NextBeforeMessageId);
