namespace ClawSharp.Core;

public sealed record McpServerResource(
    string Server,
    string Uri,
    string Name,
    string? MimeType = null,
    string? Description = null);
