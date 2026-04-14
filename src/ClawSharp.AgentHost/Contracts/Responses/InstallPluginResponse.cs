namespace ClawSharp.AgentHost.Contracts;

public sealed record InstallPluginResponse(
    string ProjectId,
    string PluginId,
    bool Enabled,
    bool Authenticated,
    string Message);
