namespace ClawSharp.Core;

public enum AppEventType
{
    SessionStarted,
    QueryReceived,
    QueryStreaming,
    QueryCompleted,
    CommandExecuted,
    ToolExecutionStarted,
    ToolExecutionProgress,
    ToolExecutionCompleted,
    TaskCreated,
    NotificationRaised
}
