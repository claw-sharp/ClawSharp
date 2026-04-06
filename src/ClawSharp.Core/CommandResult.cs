namespace ClawSharp.Core;

public sealed record CommandResult(
    bool Success,
    string Output,
    ConversationSession? SessionOverride = null);
