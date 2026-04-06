// TS origin: ./services/compact/compact.ts, ./utils/context.ts, ./services/api/claude.ts
// TS parity status: ports the compact-request max-token resolution seam so the reactive-compact path can carry the TypeScript compact cap without hardcoding it inside the eventual live summary-generation runner; the default C# implementation is the approved conservative fallback until getMaxOutputTokensForModel(...) is ported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryCompactMaxTokensResolver
{
    int Resolve(
        QueryTurnRequest request,
        QueryLoopState priorState,
        ConversationSession session,
        ClawSharpSettings settings);
}

public sealed class ConservativeQueryCompactMaxTokensResolver : IQueryCompactMaxTokensResolver
{
    public const int CompactMaxOutputTokens = 20_000;

    public int Resolve(
        QueryTurnRequest request,
        QueryLoopState priorState,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        return CompactMaxOutputTokens;
    }
}
