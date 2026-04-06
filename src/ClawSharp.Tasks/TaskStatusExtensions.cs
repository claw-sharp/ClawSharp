namespace ClawSharp.Tasks;

public static class TaskStatusExtensions
{
    public static string ToSerializedName(this TaskStatus status)
    {
        return status switch
        {
            TaskStatus.Pending => "pending",
            TaskStatus.Running => "running",
            TaskStatus.Completed => "completed",
            TaskStatus.Failed => "failed",
            TaskStatus.Killed => "killed",
            _ => throw new InvalidOperationException($"Unsupported task status '{status}'.")
        };
    }

    public static bool IsTerminal(this TaskStatus status)
    {
        return status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Killed;
    }
}
