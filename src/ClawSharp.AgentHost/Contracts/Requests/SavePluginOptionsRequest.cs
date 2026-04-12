using System.Text.Json;

namespace ClawSharp.AgentHost.Contracts;

public sealed class SavePluginOptionsRequest
{
    public string? ProjectId { get; init; }
    public string? PluginId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }
}
