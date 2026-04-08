namespace ClawSharp.AgentHost.Contracts;

public sealed class ResolveApprovalRequest
{
    public string ApprovalId { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
}
