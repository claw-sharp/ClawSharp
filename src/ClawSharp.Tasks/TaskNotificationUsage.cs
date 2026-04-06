namespace ClawSharp.Tasks;

public sealed record TaskNotificationUsage(
    int TotalTokens,
    int ToolUses,
    int DurationMs);
