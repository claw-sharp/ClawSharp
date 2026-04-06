// TS parity status: ports the internal post-sampling hook registry contract from TypeScript; hooks remain an internal programmatic API and do not change query control flow.
namespace ClawSharp.Query;

public delegate ValueTask PostSamplingHook(ReplHookContext context);

public interface IPostSamplingHookRegistry
{
    void Register(PostSamplingHook hook);

    void Clear();

    Task ExecuteAsync(ReplHookContext context);
}
