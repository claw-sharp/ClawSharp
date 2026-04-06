namespace ClawSharp.Core;

public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string? MimeType = null,
    string? Description = null);
