namespace ClawSharp.AgentHost.Contracts;

public sealed class InstallPluginRequest
{
    public string? ProjectId { get; set; }
    public string PluginId { get; set; } = string.Empty;
}
