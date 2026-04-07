// TS parity status: ports the pure TypeScript max_turns_reached decision branch used before continuation and on aborted-tools exit; wiring this into the live model-backed loop remains blocked.
namespace ClawSharp.Query;

public static class QueryMaxTurnsPolicy
{
    public static QueryMaxTurnsReachedNotification? Evaluate(
        int? maxTurns,
        int nextTurnCount)
    {
        if (maxTurns is null || nextTurnCount <= maxTurns.Value)
        {
            return null;
        }

        return new QueryMaxTurnsReachedNotification(
            maxTurns.Value,
            nextTurnCount);
    }
}
