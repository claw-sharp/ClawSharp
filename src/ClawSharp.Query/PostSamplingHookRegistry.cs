// TS origin: ./utils/hooks/postSamplingHooks.ts
// TS parity status: ports the in-memory post-sampling hook registry and sequential execution order from TypeScript; hook failures are swallowed after being forwarded to the injected error reporter, matching the non-blocking TS behavior.
namespace ClawSharp.Query;

public sealed class PostSamplingHookRegistry : IPostSamplingHookRegistry
{
    private readonly List<PostSamplingHook> _hooks = [];
    private readonly Action<Exception>? _reportError;

    public PostSamplingHookRegistry(Action<Exception>? reportError = null)
    {
        _reportError = reportError;
    }

    public void Register(PostSamplingHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks.Add(hook);
    }

    public void Clear()
    {
        _hooks.Clear();
    }

    public async Task ExecuteAsync(ReplHookContext context)
    {
        foreach (var hook in _hooks)
        {
            try
            {
                await hook(context);
            }
            catch (Exception exception)
            {
                _reportError?.Invoke(exception);
            }
        }
    }
}
