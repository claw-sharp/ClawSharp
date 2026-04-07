namespace ClawSharp.Core;

public sealed record AgentMetadata(
    string AgentType,
    string? WorktreePath = null,
    string? Description = null);
