// TS origin: ./types/tools.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpToolProgressNotification(
    double? Progress = null,
    double? Total = null,
    string? ProgressMessage = null);
