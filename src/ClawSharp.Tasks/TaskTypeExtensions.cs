// TS origin: ./Task.ts
namespace ClawSharp.Tasks;

public static class TaskTypeExtensions
{
    public static string ToSerializedName(this TaskType taskType)
    {
        return taskType switch
        {
            TaskType.LocalBash => "local_bash",
            TaskType.LocalAgent => "local_agent",
            TaskType.RemoteAgent => "remote_agent",
            TaskType.InProcessTeammate => "in_process_teammate",
            TaskType.LocalWorkflow => "local_workflow",
            TaskType.MonitorMcp => "monitor_mcp",
            TaskType.Dream => "dream",
            _ => throw new InvalidOperationException($"Unsupported task type '{taskType}'.")
        };
    }
}
