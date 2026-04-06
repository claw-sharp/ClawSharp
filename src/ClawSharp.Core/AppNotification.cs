namespace ClawSharp.Core;

public sealed record AppNotification(
    NotificationLevel Level,
    string Message,
    DateTimeOffset Timestamp);
