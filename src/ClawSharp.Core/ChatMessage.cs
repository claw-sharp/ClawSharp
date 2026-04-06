// TS origin: ./types/message.ts, ./utils/messages.ts
namespace ClawSharp.Core;

public sealed record ChatMessage(
    string Id,
    MessageRole Role,
    IReadOnlyList<MessageContentBlock> ContentBlocks,
    DateTimeOffset Timestamp)
{
    public string Content =>
        string.Join(
            Environment.NewLine,
            ContentBlocks
                .Where(
                    block =>
                        block.Kind == MessageContentKind.Text ||
                        block.Kind == MessageContentKind.ToolResult)
                .Select(block => block.Value));
}
