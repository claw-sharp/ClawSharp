// TS origin: ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpPromptDefinition(
    string Name,
    string? Description = null,
    IReadOnlyList<McpPromptArgumentDefinition>? Arguments = null);
