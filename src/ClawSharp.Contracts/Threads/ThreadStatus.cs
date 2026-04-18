namespace ClawSharp.Contracts.Threads;

public enum ThreadStatus
{
    Idle,
    Running,
    WaitingApproval,
    Completed,
    Failed,
    Scheduled
}
