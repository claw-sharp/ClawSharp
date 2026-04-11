namespace ClawSharp.AgentHost.Contracts;

public sealed class UpdateSettingsRequest
{
    public string? ProjectId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? FallbackModel { get; init; }
    public bool? EnableTelemetry { get; init; }
    public bool? FileCheckpointingEnabled { get; init; }
    public string? ApiKey { get; init; }
    public string? AuthToken { get; init; }
    public string? AccountId { get; init; }
    public bool? ClearApiKey { get; init; }
    public bool? ClearAuthToken { get; init; }
    public bool? ClearAccountId { get; init; }
    public bool? UseExternalCredential { get; init; }
}
