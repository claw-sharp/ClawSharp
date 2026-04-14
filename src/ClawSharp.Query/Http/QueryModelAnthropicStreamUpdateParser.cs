// TS parity status: ports the concrete Anthropic streamed-event parser beneath the C# model-call executor for text deltas, per-content-block assistant message emission on `content_block_stop`, tool_use content accumulation, assistant API-error message emission, and completed-attempt message finalization; thinking, server_tool_use, and fallback retry branches remain intentionally unported.
using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryModelAnthropicStreamUpdateParser : IQueryModelStreamUpdateParser
{
    private readonly Dictionary<int, PartialAssistantContentBlock> _contentBlocks = [];
    private readonly List<ChatMessage> _emittedMessages = [];
    private readonly List<string> _responseOutputItems = [];
    private int? _outputTokens;
    private string? _responseId;

    public void Reset()
    {
        _contentBlocks.Clear();
        _emittedMessages.Clear();
        _responseOutputItems.Clear();
        _outputTokens = null;
        _responseId = null;
    }

    public IReadOnlyList<QueryModelCallUpdate> Parse(JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var updates = new List<QueryModelCallUpdate>();

        switch (payload["type"]?.GetValue<string>())
        {
            case "message_start":
            case "message_delta":
                UpdateOutputTokens(payload);
                UpdateResponseId(payload);
                UpdateResponseOutputItems(payload);
                break;
            case "content_block_start":
                RegisterContentBlock(payload);
                break;
            case "content_block_delta":
                AppendContentBlockDelta(payload, updates);
                break;
            case "content_block_stop":
                EmitCompletedContentBlock(payload, updates);
                break;
            case "message_stop":
                break;
            case "error":
                EmitAssistantApiErrorMessage(payload, updates);
                break;
        }

        updates.Add(new QueryModelCallUpdate(new QueryStreamEventRuntimeEvent(new QueryStreamEvent(payload.DeepClone()))));
        return updates;
    }

    public IReadOnlyList<QueryModelCallUpdate> Complete(QueryLoopState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var updates = new List<QueryModelCallUpdate>();
        var finalState = state with
        {
            Messages = state.Messages.Concat(_emittedMessages).ToArray()
        };

        updates.Add(
            new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        finalState),
                    _outputTokens,
                    _responseId,
                    _responseOutputItems.Count == 0 ? null : _responseOutputItems.ToArray())));

        Reset();
        return updates;
    }

    private void RegisterContentBlock(JsonNode payload)
    {
        var index = payload["index"]?.GetValue<int>() ?? 0;
        var contentBlock = payload["content_block"];
        if (contentBlock is null)
        {
            return;
        }

        var blockType = contentBlock["type"]?.GetValue<string>();
        if (string.Equals(blockType, "text", StringComparison.Ordinal))
        {
            _contentBlocks[index] = PartialAssistantContentBlock.ForText();
            return;
        }

        if (string.Equals(blockType, "tool_use", StringComparison.Ordinal))
        {
            _contentBlocks[index] = PartialAssistantContentBlock.ForToolUse(
                contentBlock["name"]?.GetValue<string>() ?? string.Empty,
                contentBlock["id"]?.GetValue<string>() ?? string.Empty,
                contentBlock["tool_call_item_id"]?.GetValue<string>(),
                string.Empty);
            return;
        }

        if (string.Equals(blockType, "server_tool_use", StringComparison.Ordinal))
        {
            _contentBlocks[index] = PartialAssistantContentBlock.ForServerToolUse(
                contentBlock["name"]?.GetValue<string>() ?? string.Empty,
                contentBlock["id"]?.GetValue<string>() ?? string.Empty);
            return;
        }

        if (string.Equals(blockType, "web_search_tool_result", StringComparison.Ordinal))
        {
            _contentBlocks[index] = PartialAssistantContentBlock.ForWebSearchToolResult(
                contentBlock["tool_use_id"]?.GetValue<string>() ?? string.Empty,
                contentBlock["content"]?.ToJsonString() ?? string.Empty);
        }
    }

    private void AppendContentBlockDelta(JsonNode payload, List<QueryModelCallUpdate> updates)
    {
        var index = payload["index"]?.GetValue<int>() ?? 0;
        if (!_contentBlocks.TryGetValue(index, out var block))
        {
            throw new InvalidOperationException("Content block not found.");
        }

        var delta = payload["delta"];
        var deltaType = delta?["type"]?.GetValue<string>();
        if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
        {
            if (block.Kind != MessageContentKind.Text)
            {
                throw new InvalidOperationException("Content block is not a text block.");
            }

            var text = delta?["text"]?.GetValue<string>() ?? string.Empty;
            block.TextBuilder.Append(text);
            if (text.Length > 0)
            {
                updates.Add(new QueryModelCallUpdate(new QueryStreamDeltaRuntimeEvent(text)));
            }

            return;
        }

        if (string.Equals(deltaType, "input_json_delta", StringComparison.Ordinal))
        {
            if (block.Kind != MessageContentKind.ToolUse)
            {
                throw new InvalidOperationException("Content block is not an input_json block.");
            }

            var partialJson = delta?["partial_json"]?.GetValue<string>() ?? string.Empty;
            block.InputBuilder.Append(partialJson);
        }
    }

    private void EmitCompletedContentBlock(JsonNode payload, List<QueryModelCallUpdate> updates)
    {
        var index = payload["index"]?.GetValue<int>() ?? 0;
        if (!_contentBlocks.TryGetValue(index, out var block))
        {
            throw new InvalidOperationException("Content block not found.");
        }

        var assistantMessage = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.Assistant,
            [block.ToMessageContentBlock()],
            DateTimeOffset.UtcNow);
        _emittedMessages.Add(assistantMessage);
        updates.Add(new QueryModelCallUpdate(new QueryMessageRuntimeEvent(assistantMessage)));
    }

    private void EmitAssistantApiErrorMessage(JsonNode payload, List<QueryModelCallUpdate> updates)
    {
        var error = payload["error"];
        var apiError = error?["type"]?.GetValue<string>();
        var content = error?["message"]?.GetValue<string>() ?? string.Empty;
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(
            content,
            apiError: apiError,
            error: apiError,
            errorDetails: error?.ToJsonString());
        _emittedMessages.Add(message);

        if (!string.Equals(apiError, "max_output_tokens", StringComparison.Ordinal))
        {
            updates.Add(new QueryModelCallUpdate(new QueryMessageRuntimeEvent(message)));
        }
    }

    private void UpdateOutputTokens(JsonNode payload)
    {
        if (TryGetOutputTokens(payload["usage"], out var outputTokens) ||
            TryGetOutputTokens(payload["message"]?["usage"], out outputTokens))
        {
            _outputTokens = outputTokens;
        }
    }

    private void UpdateResponseId(JsonNode payload)
    {
        if (string.IsNullOrWhiteSpace(_responseId))
        {
            _responseId = payload["response_id"]?.GetValue<string>();
        }
    }

    private void UpdateResponseOutputItems(JsonNode payload)
    {
        if (_responseOutputItems.Count > 0)
        {
            return;
        }

        var items = payload["response_output_items"]?.AsArray();
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            _responseOutputItems.Add(item.ToJsonString());
        }
    }

    private static bool TryGetOutputTokens(JsonNode? usage, out int outputTokens)
    {
        outputTokens = 0;
        if (usage?["output_tokens"] is null)
        {
            return false;
        }

        try
        {
            outputTokens = usage["output_tokens"]!.GetValue<int>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PartialAssistantContentBlock
    {
        private PartialAssistantContentBlock(
            MessageContentKind kind,
            string? name = null,
            string? toolUseId = null,
            string? toolCallItemId = null)
        {
            Kind = kind;
            Name = name;
            ToolUseId = toolUseId;
            ToolCallItemId = toolCallItemId;
        }

        public MessageContentKind Kind { get; }
        public string? Name { get; }
        public string? ToolUseId { get; }
        public string? ToolCallItemId { get; }
        public StringBuilder TextBuilder { get; } = new();
        public StringBuilder InputBuilder { get; } = new();

        public static PartialAssistantContentBlock ForText()
        {
            var block = new PartialAssistantContentBlock(MessageContentKind.Text);
            return block;
        }

        public static PartialAssistantContentBlock ForToolUse(
            string name,
            string toolUseId,
            string? toolCallItemId,
            string input)
        {
            var block = new PartialAssistantContentBlock(MessageContentKind.ToolUse, name, toolUseId, toolCallItemId);
            block.InputBuilder.Append(input);
            return block;
        }

        public static PartialAssistantContentBlock ForServerToolUse(string name, string toolUseId)
        {
            return new PartialAssistantContentBlock(MessageContentKind.ToolUse, name, toolUseId);
        }

        public static PartialAssistantContentBlock ForWebSearchToolResult(string toolUseId, string content)
        {
            var block = new PartialAssistantContentBlock(MessageContentKind.WebSearchToolResult, toolUseId: toolUseId);
            block.InputBuilder.Append(content);
            return block;
        }

        public MessageContentBlock ToMessageContentBlock()
        {
            return Kind switch
            {
                MessageContentKind.Text => new MessageContentBlock(
                    MessageContentKind.Text,
                    TextBuilder.ToString()),
                MessageContentKind.ToolUse => new MessageContentBlock(
                    MessageContentKind.ToolUse,
                    InputBuilder.ToString(),
                    Name,
                    BuildToolUseMetadata()),
                MessageContentKind.WebSearchToolResult => new MessageContentBlock(
                    MessageContentKind.WebSearchToolResult,
                    InputBuilder.ToString(),
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["toolUseId"] = ToolUseId ?? string.Empty
                    }),
                _ => throw new InvalidOperationException($"Unsupported assistant content block kind '{Kind}'.")
            };
        }

        private Dictionary<string, string> BuildToolUseMetadata()
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["toolUseId"] = ToolUseId ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(ToolCallItemId))
            {
                metadata["toolCallItemId"] = ToolCallItemId!;
            }

            return metadata;
        }
    }
}
