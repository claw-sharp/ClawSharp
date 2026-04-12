namespace ClawSharp.AgentHost.Contracts;

public sealed class DeletePluginOptionsRequest
{
    public string? ProjectId { get; init; }
    public string? PluginId { get; init; }
}
