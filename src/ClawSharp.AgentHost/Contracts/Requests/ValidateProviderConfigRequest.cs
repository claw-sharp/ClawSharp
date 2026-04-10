namespace ClawSharp.AgentHost.Contracts;

public sealed class ValidateProviderConfigRequest
{
    public string? ProjectId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? Model { get; init; }
}
