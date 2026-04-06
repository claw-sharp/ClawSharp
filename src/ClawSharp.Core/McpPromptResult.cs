// TS origin: ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpPromptResult(
    IReadOnlyList<ChatMessage> Messages);
