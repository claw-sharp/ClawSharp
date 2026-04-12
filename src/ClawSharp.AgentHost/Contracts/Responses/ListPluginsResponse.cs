namespace ClawSharp.AgentHost.Contracts;

public sealed record ListPluginsResponse(
    string ProjectId,
    string WorkspaceRoot,
    IReadOnlyList<PluginSummaryDto> Plugins);
