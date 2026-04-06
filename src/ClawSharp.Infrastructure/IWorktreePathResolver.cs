// TS origin: ./utils/getWorktreePaths.ts
namespace ClawSharp.Infrastructure;

public interface IWorktreePathResolver
{
    Task<IReadOnlyList<string>> GetWorktreePathsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
