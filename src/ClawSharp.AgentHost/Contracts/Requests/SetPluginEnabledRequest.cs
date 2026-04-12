namespace ClawSharp.AgentHost.Contracts;

public sealed class SetPluginEnabledRequest
{
    public string? ProjectId { get; init; }
    public string? PluginId { get; init; }
    public bool Enabled { get; init; }
}
