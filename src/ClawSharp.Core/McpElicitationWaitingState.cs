namespace ClawSharp.Core;

public sealed record McpElicitationWaitingState(
    string ActionLabel,
    bool ShowCancel = false);
