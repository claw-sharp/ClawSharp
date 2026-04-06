namespace ClawSharp.Core;

public sealed record McpPromptArgumentDefinition(
    string Name,
    bool Required = false,
    string? Description = null);
