namespace ClawSharp.Contracts.Runs;

public enum RunEventKind
{
    Started,
    TextDelta,
    MessageCompleted,
    ToolProgress,
    ToolResult,
    Completed,
    Failed
}
