namespace ClawSharp.Contracts.Settings;

public sealed record RemoteRuntimeSettings(
    string Provider,
    string Model,
    string? FallbackModel,
    string PermissionMode,
    bool EnableTelemetry,
    string BaseUrl,
    string Transport);
