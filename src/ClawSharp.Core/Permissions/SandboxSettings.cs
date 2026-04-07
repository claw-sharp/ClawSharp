namespace ClawSharp.Core;

public sealed class SandboxSettings
{
    public bool Enabled { get; init; }
    public bool AllowUnsandboxedCommands { get; init; } = true;
    public bool FailIfUnavailable { get; init; }
}
