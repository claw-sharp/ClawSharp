namespace ClawSharp.Core;

public sealed record DiscoveredSkill(
    string Name,
    string FilePath,
    string BaseDirectory,
    string Source);
