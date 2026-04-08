namespace ClawSharp.AgentHost.Contracts;

public sealed class UpdateSettingsRequest
{
    public string? ProjectId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? FallbackModel { get; init; }
    public bool? EnableTelemetry { get; init; }
    public bool? FileCheckpointingEnabled { get; init; }
}
