// TS parity status: defines the C# boundary for building the default REPL model-turn context from the TypeScript system-prompt and cached context contract.
namespace ClawSharp.Query;

public interface IQueryModelTurnContextProvider
{
    Task<QueryModelTurnContext> GetReplMainThreadContextAsync(CancellationToken cancellationToken = default);
}
