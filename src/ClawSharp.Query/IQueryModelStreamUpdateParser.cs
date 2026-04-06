// TS parity status: ports the raw streamed model-event parsing boundary beneath the C# model-call executor, including attempt-local parser state reset and completion; concrete fallback and retry behavior still remains intentionally unported.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public interface IQueryModelStreamUpdateParser
{
    void Reset();

    IReadOnlyList<QueryModelCallUpdate> Parse(JsonNode payload);

    IReadOnlyList<QueryModelCallUpdate> Complete(QueryLoopState state);
}
