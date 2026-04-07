namespace ClawSharp.Core;

public sealed record McpServerConnectionStatistics(
    int TotalServers,
    int StdioCount,
    int SseCount,
    int HttpCount,
    int SseIdeCount,
    int WsIdeCount);
