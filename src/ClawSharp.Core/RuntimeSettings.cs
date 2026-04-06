// TS origin: ./utils/config.ts
namespace ClawSharp.Core;

public sealed class RuntimeSettings
{
    public PermissionMode PermissionMode { get; init; } = PermissionMode.Default;
    public string Model { get; init; } = "foundation-placeholder";
    public string? FallbackModel { get; init; }
    public bool EnableTelemetry { get; init; } = false;
    public bool FileCheckpointingEnabled { get; init; } = true;
    public bool? AutoMemoryEnabled { get; init; }
    public string? AutoMemoryDirectory { get; init; }
}
