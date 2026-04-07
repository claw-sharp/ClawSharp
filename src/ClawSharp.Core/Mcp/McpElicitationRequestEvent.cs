namespace ClawSharp.Core;

public sealed record McpElicitationRequestEvent(
    string ServerName,
    string RequestId,
    McpElicitRequestParams Params,
    CancellationToken CancellationToken,
    Func<McpElicitResult, bool> Respond,
    McpElicitationWaitingState? WaitingState = null,
    Action<string>? OnWaitingDismiss = null,
    bool Completed = false);
