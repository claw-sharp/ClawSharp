// TS origin: no direct 1:1 source yet; notification behavior is planned from ./query.ts and ./utils/messageQueueManager.ts.
namespace ClawSharp.Core;

public sealed record AppNotification(
    NotificationLevel Level,
    string Message,
    DateTimeOffset Timestamp);
