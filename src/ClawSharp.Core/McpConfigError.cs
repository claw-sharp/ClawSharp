// TS origin: ./services/mcp/config.ts
namespace ClawSharp.Core;

public enum McpConfigErrorSeverity
{
    Warning,
    Fatal
}

public sealed record McpConfigErrorMetadata(
    McpConfigScope Scope,
    string? ServerName,
    McpConfigErrorSeverity Severity);

public sealed record McpConfigError(
    string? File,
    string Path,
    string Message,
    string? Suggestion,
    McpConfigErrorMetadata Metadata);
