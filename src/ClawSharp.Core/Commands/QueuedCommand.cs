namespace ClawSharp.Core;

public sealed record QueuedCommand(
    string Value,
    PromptInputMode Mode,
    QueuePriority? Priority = null);
