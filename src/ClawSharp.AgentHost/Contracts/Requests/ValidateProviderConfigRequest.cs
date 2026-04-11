namespace ClawSharp.AgentHost.Contracts;

public sealed class ValidateProviderConfigRequest
{
    public string? ProjectId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? Model { get; init; }
    public bool LiveCheck { get; init; }
    public string? ApiKey { get; init; }
    public string? AuthToken { get; init; }
    public string? AccountId { get; init; }
    public bool? UseExternalCredential { get; init; }
}
