using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Approvals;

public sealed class ApprovalCatalogService
{
    private readonly JsonApprovalRequestStore _approvalStore = new();

    public async Task<ListPendingApprovalsResponse> ListPendingApprovalsAsync(
        ListPendingApprovalsRequest request,
        CancellationToken cancellationToken = default)
    {
        var approvals = await _approvalStore.LoadAsync(cancellationToken);
        return new ListPendingApprovalsResponse(
            approvals
                .Where(static approval => approval.Decision == ApprovalDecision.Pending)
                .Select(MapApproval)
                .OrderByDescending(approval => approval.CreatedAt)
                .ToArray());
    }

    public async Task<ResolveApprovalResponse> ResolveApprovalAsync(
        ResolveApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ApprovalId))
        {
            throw new AgentHostException("invalid_request", "approvalId is required.");
        }

        var approvals = (await _approvalStore.LoadAsync(cancellationToken)).ToList();
        var approval = approvals.FirstOrDefault(item => string.Equals(item.Id, request.ApprovalId, StringComparison.Ordinal));
        if (approval is null)
        {
            throw new AgentHostException("approval_not_found", $"Approval '{request.ApprovalId}' was not found.");
        }

        var decision = request.Decision.Trim().ToLowerInvariant() switch
        {
            "approved" or "approve" => ApprovalDecision.Approved,
            "always_allow" or "always-allow" or "approve_for_session" or "approved_for_session" => ApprovalDecision.ApprovedForSession,
            "rejected" or "reject" => ApprovalDecision.Rejected,
            _ => throw new AgentHostException("invalid_request", $"Unsupported approval decision '{request.Decision}'.")
        };

        var updated = approval with { Decision = decision };
        var index = approvals.FindIndex(item => string.Equals(item.Id, request.ApprovalId, StringComparison.Ordinal));
        approvals[index] = updated;
        _approvalStore.Save(approvals);
        return new ResolveApprovalResponse(MapApproval(updated));
    }

    private static ApprovalRequestDto MapApproval(ApprovalRequest approval)
    {
        return new ApprovalRequestDto(
            approval.Id,
            approval.Action,
            approval.Decision.ToString(),
            approval.CreatedAt);
    }
}
