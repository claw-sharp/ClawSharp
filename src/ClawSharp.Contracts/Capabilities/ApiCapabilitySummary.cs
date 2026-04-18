namespace ClawSharp.Contracts.Capabilities;

public sealed record ApiCapabilitySummary(
    string Key,
    string Name,
    bool Enabled);
