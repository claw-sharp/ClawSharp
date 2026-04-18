namespace ClawSharp.Contracts.Health;

public sealed record ApiHealthResponse(
    string Service,
    string Status,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> SupportedClientModes);
