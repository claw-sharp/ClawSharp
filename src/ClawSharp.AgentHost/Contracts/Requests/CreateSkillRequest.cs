namespace ClawSharp.AgentHost.Contracts;

public sealed class CreateSkillRequest
{
    public string? ProjectId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Instructions { get; init; }
}
