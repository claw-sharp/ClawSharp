namespace ClawSharp.Core;

public interface IApprovalRequestStore
{
    IReadOnlyList<ApprovalRequest> Load();

    Task<IReadOnlyList<ApprovalRequest>> LoadAsync(CancellationToken cancellationToken = default);

    void Save(IReadOnlyList<ApprovalRequest> requests);
}
