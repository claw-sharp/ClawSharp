namespace ClawSharp.Query;

public sealed record QueryLoopTerminal(
    QueryTerminalReason Reason,
    int? TurnCount = null,
    string? ErrorMessage = null);
