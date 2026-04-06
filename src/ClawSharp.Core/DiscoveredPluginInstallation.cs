namespace ClawSharp.Core;

public sealed record DiscoveredPluginInstallation(
    string PluginId,
    PluginInstallationScope Scope,
    string InstallPath,
    string? ProjectPath,
    string? Version,
    string? InstalledAt,
    string? LastUpdated,
    string? GitCommitSha);
