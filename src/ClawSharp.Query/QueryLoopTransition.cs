namespace ClawSharp.Query;

public sealed record QueryLoopTransition(
    QueryContinueReason Reason,
    int? Attempt = null,
    int? Committed = null);
