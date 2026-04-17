namespace ClawSharp.AgentHost.Contracts;

public sealed class CreateAgentRequest
{
    public string? ProjectId { get; init; }
    public string? Identifier { get; init; }
    public string? WhenToUse { get; init; }
    public string? SystemPrompt { get; init; }
    public string? Model { get; init; }
    public string? Color { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    public IReadOnlyList<string>? DisallowedTools { get; init; }
    public IReadOnlyList<string>? Skills { get; init; }
    public string? PermissionMode { get; init; }
    public int? MaxTurns { get; init; }
    public bool? Background { get; init; }
    public string? InitialPrompt { get; init; }
    public string? Memory { get; init; }
    public string? Isolation { get; init; }
    public bool? OmitClaudeMd { get; init; }
}
