namespace ClawSharp.AgentHost.Contracts;

public sealed record HealthResponse(
    string HostName,
    string HostVersion,
    string ProtocolVersion,
    string CurrentWorkingDirectory,
    IReadOnlyList<string> SupportedCommands);
