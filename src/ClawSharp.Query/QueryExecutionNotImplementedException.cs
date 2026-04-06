// TS origin: ./QueryEngine.ts, ./query.ts
// TS parity status: ClawSharp-specific guard until the model-backed query loop is ported 1:1 from TypeScript.
namespace ClawSharp.Query;

public sealed class QueryExecutionNotImplementedException : NotSupportedException
{
    public QueryExecutionNotImplementedException()
        : base("Model-backed query execution is not implemented yet. Use translated slash commands until the TypeScript query loop is ported.")
    {
    }
}
