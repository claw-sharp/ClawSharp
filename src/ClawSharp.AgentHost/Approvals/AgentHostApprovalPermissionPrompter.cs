using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Approvals;

public sealed class AgentHostApprovalPermissionPrompter : IPermissionPrompter
{
    private readonly JsonApprovalRequestStore _approvalStore;
    private readonly AgentHostEventDispatcher _eventDispatcher;
    private readonly SemaphoreSlim _storeGate = new(1, 1);

    public AgentHostApprovalPermissionPrompter(
        AgentHostEventDispatcher eventDispatcher,
        JsonApprovalRequestStore? approvalStore = null)
    {
        _eventDispatcher = eventDispatcher;
        _approvalStore = approvalStore ?? new JsonApprovalRequestStore();
    }

    public async Task<PromptPermissionDecision> PromptAsync(string message, CancellationToken cancellationToken = default)
    {
        var approval = new ApprovalRequest(
            Guid.NewGuid().ToString("N"),
            message,
            ApprovalDecision.Pending,
            DateTimeOffset.UtcNow);

        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            var approvals = (await _approvalStore.LoadAsync(cancellationToken)).ToList();
            approvals.Add(approval);
            _approvalStore.Save(approvals);
        }
        finally
        {
            _storeGate.Release();
        }

        await _eventDispatcher.PublishAsync(
            "ApprovalRequested",
            new ApprovalRequestDto(
                approval.Id,
                approval.Action,
                approval.Decision.ToString(),
                approval.CreatedAt),
            CancellationToken.None);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = (await _approvalStore.LoadAsync(cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Id, approval.Id, StringComparison.Ordinal));
            if (current is null)
            {
                return PromptPermissionDecision.Deny;
            }

            switch (current.Decision)
            {
                case ApprovalDecision.Approved:
                    return PromptPermissionDecision.Allow;
                case ApprovalDecision.Rejected:
                    return PromptPermissionDecision.Deny;
                case ApprovalDecision.Pending:
                default:
                    await Task.Delay(250, cancellationToken);
                    break;
            }
        }
    }
}
