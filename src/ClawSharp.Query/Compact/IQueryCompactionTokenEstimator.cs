// TS parity status: ports a dedicated compaction token-estimation seam so the reactive-compact path can carry the TypeScript preCompactTokenCount boundary input without inventing it at the call site; the default C# implementation is an approved approximate substitute until the exact TS token-counting helpers are ported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryCompactionTokenEstimator
{
    int Estimate(IReadOnlyList<ChatMessage> messages);
}

public sealed class ApproximateQueryCompactionTokenEstimator : IQueryCompactionTokenEstimator
{
    public int Estimate(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var estimatedTokens = 0;
        foreach (var message in messages)
        {
            estimatedTokens += 4; // rough per-message envelope
            estimatedTokens += EstimateString(message.Role.ToString());

            foreach (var block in message.ContentBlocks)
            {
                estimatedTokens += 2; // rough per-block envelope
                estimatedTokens += EstimateString(block.Kind.ToString());
                estimatedTokens += EstimateString(block.Name);
                estimatedTokens += EstimateString(block.Value);

                if (block.Metadata is null)
                {
                    continue;
                }

                foreach (var pair in block.Metadata)
                {
                    estimatedTokens += EstimateString(pair.Key);
                    estimatedTokens += EstimateString(pair.Value);
                }
            }
        }

        return Math.Max(estimatedTokens, 1);
    }

    private static int EstimateString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return (int)Math.Ceiling(value.Length / 4d);
    }
}
