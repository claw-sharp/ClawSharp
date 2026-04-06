using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed class TranscriptMessageRenderer
{
    private readonly TaskNotificationMessageRenderer _taskNotificationMessageRenderer = new();

    public IReadOnlyList<string> Render(ChatMessage message, MessageRole? previousRole = null)
    {
        if (message.ContentBlocks.Count == 0)
        {
            return [];
        }

        var visibleBlocks = message.ContentBlocks
            .Where(
                static block =>
                    block.Kind is MessageContentKind.Text or MessageContentKind.Attachment or MessageContentKind.ToolResult)
            .ToArray();
        if (visibleBlocks.Length == 0)
        {
            return [];
        }

        var lines = new List<string>();
        if (previousRole is not null && previousRole != message.Role)
        {
            lines.Add(string.Empty);
        }

        var isContinuation = previousRole == message.Role;
        var isFirstRenderedLine = true;
        foreach (var block in visibleBlocks)
        {
            if (string.IsNullOrWhiteSpace(block.Value))
            {
                continue;
            }

            var renderedTaskNotification = _taskNotificationMessageRenderer.TryRender(block.Value);
            if (!string.IsNullOrWhiteSpace(renderedTaskNotification))
            {
                lines.Add($"! {renderedTaskNotification}");
                isFirstRenderedLine = false;
                continue;
            }

            var blockLines = block.Value
                .Split(Environment.NewLine)
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(
                    line =>
                    {
                        var renderedLine = RenderLine(message.Role, line, isContinuation, isFirstRenderedLine);
                        isFirstRenderedLine = false;
                        return renderedLine;
                    })
                .ToArray();
            if (blockLines.Length == 0)
            {
                continue;
            }

            lines.AddRange(blockLines);
        }

        return lines;
    }

    private static string RenderLine(
        MessageRole role,
        string line,
        bool isContinuation,
        bool isFirstRenderedLine)
    {
        return $"{GetLinePrefix(role, isContinuation, isFirstRenderedLine)}{line}";
    }

    private static string GetLinePrefix(
        MessageRole role,
        bool isContinuation,
        bool isFirstRenderedLine)
    {
        if (role == MessageRole.User)
        {
            return "> ";
        }

        if (!isFirstRenderedLine)
        {
            return "  ";
        }

        return role switch
        {
            MessageRole.Assistant when isContinuation => "  ",
            MessageRole.Assistant => "* ",
            MessageRole.System when isContinuation => "  ",
            MessageRole.System => "! ",
            _ => string.Empty
        };
    }
}
