namespace ClawSharp.Core;

public sealed record McpPromptResult(
    IReadOnlyList<ChatMessage> Messages);
