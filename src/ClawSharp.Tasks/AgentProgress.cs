// TS origin: ./tasks/LocalAgentTask/LocalAgentTask.tsx
namespace ClawSharp.Tasks;

public sealed record ToolActivity(
    string ToolName,
    IReadOnlyDictionary<string, object?> Input,
    string? ActivityDescription = null,
    bool? IsSearch = null,
    bool? IsRead = null);

public sealed record AgentProgress(
    int ToolUseCount,
    int TokenCount,
    ToolActivity? LastActivity = null,
    IReadOnlyList<ToolActivity>? RecentActivities = null,
    string? Summary = null);
