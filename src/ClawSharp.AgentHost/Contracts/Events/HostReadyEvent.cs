namespace ClawSharp.AgentHost.Contracts;

public sealed record HostReadyEvent(
    string HostName,
    string HostVersion,
    string ProtocolVersion);
