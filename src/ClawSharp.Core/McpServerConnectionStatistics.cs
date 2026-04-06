// TS origin: ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpServerConnectionStatistics(
    int TotalServers,
    int StdioCount,
    int SseCount,
    int HttpCount,
    int SseIdeCount,
    int WsIdeCount);
