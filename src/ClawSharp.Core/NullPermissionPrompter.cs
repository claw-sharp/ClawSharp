using System.Threading;
using System.Threading.Tasks;

namespace ClawSharp.Core;

public sealed class NullPermissionPrompter : IPermissionPrompter
{
    public Task<PromptPermissionDecision> PromptAsync(string message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PromptPermissionDecision.Deny);
    }
}
