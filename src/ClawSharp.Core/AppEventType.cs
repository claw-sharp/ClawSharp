// TS origin: no direct 1:1 source yet; event flow is currently derived from ./query.ts, ./QueryEngine.ts, and ./utils/commandLifecycle.ts.
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
