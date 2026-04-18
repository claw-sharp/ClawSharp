namespace ClawSharp.Contracts.Capabilities;

public sealed record ApiCapabilitiesResponse(
    IReadOnlyList<ApiCapabilitySummary> Items);
