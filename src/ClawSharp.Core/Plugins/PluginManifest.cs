namespace ClawSharp.Core;

public sealed record PluginMcpServerDefinition(
    string Name,
    McpServerConfig Config);

public sealed record PluginManifest(
    string Name,
    string? Description,
    string? Version,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> OutputStyles,
    IReadOnlyList<string> HookFiles,
    IReadOnlyList<PluginMcpServerDefinition> McpServers,
    IReadOnlyDictionary<string, object?>? Settings = null,
    IReadOnlyDictionary<string, PluginOptionDefinition>? UserConfig = null);

public sealed record PluginValidationIssue(
    string Path,
    string Message,
    bool IsWarning = false);

public sealed record DiscoveredPlugin(
    string PluginId,
    string Name,
    string InstallPath,
    bool Enabled,
    bool IsBundled,
    PluginManifest? Manifest,
    IReadOnlyList<PluginValidationIssue> ValidationIssues,
    IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> Hooks,
    IReadOnlyDictionary<string, object?>? Settings = null,
    IReadOnlyDictionary<string, PluginOptionDefinition>? UserConfig = null);
