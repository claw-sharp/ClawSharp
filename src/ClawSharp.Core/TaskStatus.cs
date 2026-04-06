// TS origin: ./tasks/LocalShellTask/LocalShellTask.tsx, ./tasks/LocalAgentTask/LocalAgentTask.tsx, ./tasks/LocalMainSessionTask.ts, ./utils/task/TaskOutput.ts
namespace ClawSharp.Tasks;

public enum TaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Killed
}
