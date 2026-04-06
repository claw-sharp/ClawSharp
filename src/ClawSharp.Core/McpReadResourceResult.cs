namespace ClawSharp.Core;

public sealed record McpReadResourceResult(
    IReadOnlyList<McpResourceContent> Contents);
