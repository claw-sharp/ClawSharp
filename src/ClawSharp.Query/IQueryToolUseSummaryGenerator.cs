// TS parity status: ports the query-loop seam for deferred tool_use_summary generation so the live C# model-backed loop can carry and later emit pending summary tasks; the real summary-generation runtime and the missing non-interactive-session input remain intentionally unported.
namespace ClawSharp.Query;

public interface IQueryToolUseSummaryGenerator
{
    Task<QueryToolUseSummaryMessage?> GenerateAsync(
        QueryToolUseSummaryGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record QueryToolUseSummaryGenerationRequest(
    IReadOnlyList<QueryToolUseSummaryToolEntry> Tools,
    string? LastAssistantText);

public sealed record QueryToolUseSummaryToolEntry(
    string ToolUseId,
    string ToolName,
    string Input,
    string? Output);

public sealed class NoOpQueryToolUseSummaryGenerator : IQueryToolUseSummaryGenerator
{
    public Task<QueryToolUseSummaryMessage?> GenerateAsync(
        QueryToolUseSummaryGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<QueryToolUseSummaryMessage?>(null);
    }
}
