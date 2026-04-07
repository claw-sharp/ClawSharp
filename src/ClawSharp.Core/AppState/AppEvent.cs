namespace ClawSharp.Core;

public sealed record AppEvent(
    AppEventType Type,
    string Message,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string>? Metadata = null);
