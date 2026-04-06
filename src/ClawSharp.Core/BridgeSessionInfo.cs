// TS origin: ./bridge/bridgeMain.ts, ./bridge/replBridge.ts
namespace ClawSharp.Core;

public sealed record BridgeSessionInfo(
    string EnvironmentId,
    string SessionId,
    bool IsConnected);
