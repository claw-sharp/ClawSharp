// TS parity status: focused C# coverage for the approved conservative compact max-token resolver seam used by the reactive-compact runtime until the exact TypeScript model-capability helper is ported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryCompactMaxTokensResolverTests
{
    [Fact]
    public void Resolve_Returns_Approved_Conservative_Compact_Cap()
    {
        var resolver = new ConservativeQueryCompactMaxTokensResolver();
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-compact-max-tokens-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-compact-max-tokens", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var maxTokens = resolver.Resolve(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            session,
            new ClawSharpSettings());

        Assert.Equal(20_000, maxTokens);
    }
}
