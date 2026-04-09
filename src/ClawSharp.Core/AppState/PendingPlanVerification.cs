namespace ClawSharp.Core;

public sealed record PendingPlanVerification(
    string Plan,
    bool VerificationStarted = false,
    bool VerificationCompleted = false,
    DateTimeOffset? RequestedAt = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);
