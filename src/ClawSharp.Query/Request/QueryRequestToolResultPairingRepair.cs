using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryRequestToolResultPairingRepair
{
    public const string SyntheticToolResultPlaceholder = "[Tool result missing due to internal error]";
    private const string ToolUseInterruptedPlaceholder = "[Tool use interrupted]";
    private const string OrphanedToolResultRemovedPlaceholder = "[Orphaned tool result removed due to conversation resume]";

    public static IReadOnlyList<QueryRequestMessage> EnsureToolResultPairing(
        IReadOnlyList<QueryRequestMessage> messages,
        IReadOnlyCollection<string>? expectedToolUseIds = null)
    {
        List<QueryRequestMessage> result = [];
        HashSet<string> allSeenToolUseIds = new(StringComparer.Ordinal);
        HashSet<string> expectedIds = new(expectedToolUseIds ?? [], StringComparer.Ordinal);

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            if (!string.Equals(message.Role, "assistant", StringComparison.Ordinal))
            {
                if (string.Equals(message.Role, "user", StringComparison.Ordinal) &&
                    result.LastOrDefault() is not { Role: "assistant" } &&
                    message.Content.Any(static block => string.Equals(block.Type, "tool_result", StringComparison.Ordinal)))
                {
                    HashSet<string> seenToolResultIds = new(StringComparer.Ordinal);
                    HashSet<string> preservedToolResultIds = new(StringComparer.Ordinal);
                    var filteredContent = message.Content
                        .Where(
                            block =>
                            {
                                if (!string.Equals(block.Type, "tool_result", StringComparison.Ordinal))
                                {
                                    return true;
                                }

                                var toolUseId = block.ToolUseId;
                                if (string.IsNullOrWhiteSpace(toolUseId))
                                {
                                    return false;
                                }

                                if (expectedIds.Count == 0 || !expectedIds.Contains(toolUseId) || !seenToolResultIds.Add(toolUseId))
                                {
                                    return false;
                                }

                                preservedToolResultIds.Add(toolUseId);
                                return true;
                            })
                        .ToArray();

                    if (filteredContent.Length != message.Content.Count || expectedIds.Count > 0)
                    {
                        var expectedSyntheticBlocks = expectedIds
                            .Where(toolUseId => !preservedToolResultIds.Contains(toolUseId))
                            .Select(
                                toolUseId => new QueryRequestContentBlock(
                                    "tool_result",
                                    Text: SyntheticToolResultPlaceholder,
                                    ToolUseId: toolUseId))
                            .ToArray();
                        var patchedContent = expectedSyntheticBlocks.Concat(filteredContent).ToArray();

                        if (patchedContent.Length > 0)
                        {
                            result.Add(message with { Content = patchedContent });
                        }
                        else if (result.Count == 0)
                        {
                            result.Add(new QueryRequestMessage(
                                "user",
                                [new QueryRequestContentBlock("text", Text: OrphanedToolResultRemovedPlaceholder)]));
                        }

                        continue;
                    }
                }

                result.Add(message);
                continue;
            }

            HashSet<string> seenToolUseIds = new(StringComparer.Ordinal);
            List<QueryRequestContentBlock> finalContent = [];

            foreach (var block in message.Content)
            {
                if (!string.Equals(block.Type, "tool_use", StringComparison.Ordinal))
                {
                    finalContent.Add(block);
                    continue;
                }

                var toolUseId = block.ToolUseId;
                if (string.IsNullOrWhiteSpace(toolUseId))
                {
                    finalContent.Add(block);
                    continue;
                }

                if (allSeenToolUseIds.Contains(toolUseId))
                {
                    continue;
                }

                allSeenToolUseIds.Add(toolUseId);
                seenToolUseIds.Add(toolUseId);
                finalContent.Add(block);
            }

            if (finalContent.Count == 0)
            {
                finalContent.Add(new QueryRequestContentBlock("text", Text: ToolUseInterruptedPlaceholder));
            }

            var repairedAssistant = finalContent.Count == message.Content.Count
                ? message
                : message with { Content = finalContent.ToArray() };

            result.Add(repairedAssistant);

            var toolUseIds = seenToolUseIds.ToArray();
            var nextMessage = index + 1 < messages.Count ? messages[index + 1] : null;
            HashSet<string> existingToolResultIds = new(StringComparer.Ordinal);
            HashSet<string> duplicateToolResultIds = new(StringComparer.Ordinal);

            if (nextMessage is not null &&
                string.Equals(nextMessage.Role, "user", StringComparison.Ordinal))
            {
                foreach (var block in nextMessage.Content.Where(static block => string.Equals(block.Type, "tool_result", StringComparison.Ordinal)))
                {
                    var toolUseId = block.ToolUseId;
                    if (string.IsNullOrWhiteSpace(toolUseId))
                    {
                        continue;
                    }

                    if (!existingToolResultIds.Add(toolUseId))
                    {
                        duplicateToolResultIds.Add(toolUseId);
                    }
                }
            }

            var missingIds = toolUseIds.Where(toolUseId => !existingToolResultIds.Contains(toolUseId)).ToArray();
            var orphanedIds = existingToolResultIds.Where(toolUseId => !seenToolUseIds.Contains(toolUseId)).ToHashSet(StringComparer.Ordinal);
            if (missingIds.Length == 0 &&
                orphanedIds.Count == 0 &&
                duplicateToolResultIds.Count == 0)
            {
                continue;
            }

            var syntheticBlocks = missingIds
                .Select(
                    toolUseId => new QueryRequestContentBlock(
                        "tool_result",
                        Text: SyntheticToolResultPlaceholder,
                        ToolUseId: toolUseId))
                .ToArray();

            if (nextMessage is not null &&
                string.Equals(nextMessage.Role, "user", StringComparison.Ordinal))
            {
                HashSet<string> seenToolResultIds = new(StringComparer.Ordinal);
                var filteredContent = nextMessage.Content
                    .Where(
                        block =>
                        {
                            if (!string.Equals(block.Type, "tool_result", StringComparison.Ordinal))
                            {
                                return true;
                            }

                            var toolUseId = block.ToolUseId;
                            if (string.IsNullOrWhiteSpace(toolUseId))
                            {
                                return false;
                            }

                            if (orphanedIds.Contains(toolUseId))
                            {
                                return false;
                            }

                            return seenToolResultIds.Add(toolUseId);
                        })
                    .ToArray();

                var patchedContent = syntheticBlocks
                    .Concat(filteredContent)
                    .ToArray();

                if (patchedContent.Length > 0)
                {
                    result.Add(nextMessage with { Content = patchedContent });
                }
                else
                {
                    result.Add(new QueryRequestMessage(
                        "user",
                        [new QueryRequestContentBlock("text", Text: ChatMessageFactory.NoContentMessage)]));
                }

                index++;
                continue;
            }

            if (syntheticBlocks.Length > 0)
            {
                result.Add(new QueryRequestMessage("user", syntheticBlocks));
            }
        }

        return result;
    }
}
