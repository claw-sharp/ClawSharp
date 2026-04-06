namespace ClawSharp.Core;

public sealed record BridgeSessionInfo(
    string EnvironmentId,
    string SessionId,
    bool IsConnected);
