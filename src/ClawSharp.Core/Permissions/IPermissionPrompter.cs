using System.Threading;
using System.Threading.Tasks;

namespace ClawSharp.Core;

public enum PromptPermissionDecision 
{
    Allow,
    Deny,
    AlwaysAllow
}

public interface IPermissionPrompter
{
    Task<PromptPermissionDecision> PromptAsync(string message, CancellationToken cancellationToken = default);
}
