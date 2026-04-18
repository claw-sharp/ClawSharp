using ClawSharp.Contracts.Approvals;

namespace ClawSharp.Application.Approvals;

public interface IApprovalService
{
    Task<IReadOnlyList<ApprovalSummary>> ListPendingApprovalsAsync(
        string? threadId = null,
        CancellationToken cancellationToken = default);

    Task<ApprovalSummary> ResolveApprovalAsync(
        ResolveApprovalRequest request,
        CancellationToken cancellationToken = default);
}
