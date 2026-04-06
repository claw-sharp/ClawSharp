using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ClawSharp.Core;

public interface IInteractionService
{
    Task<string?> SelectAsync(string prompt, IEnumerable<string> options, CancellationToken cancellationToken = default);
    Task<string> AskAsync(string prompt, bool secret = false, CancellationToken cancellationToken = default);
    Task<bool> ConfirmAsync(string prompt, bool defaultValue = true, CancellationToken cancellationToken = default);
}
