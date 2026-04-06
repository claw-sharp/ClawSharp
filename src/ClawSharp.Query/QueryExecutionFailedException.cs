// TS origin: ./QueryEngine.ts, ./query.ts
namespace ClawSharp.Query;

public sealed class QueryExecutionFailedException : InvalidOperationException
{
    public QueryExecutionFailedException()
        : base("Query execution ended without a successful terminal assistant message.")
    {
    }
}
