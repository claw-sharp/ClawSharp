// TS parity status: ports the model-call request snapshot ClawSharp can currently construct without a live transport; sending and streaming the request still depend on the unported model runtime.
namespace ClawSharp.Query;

public sealed record QueryModelRequest(
    string SessionId,
    string Model,
    IReadOnlyList<QuerySystemPromptBlock> System,
    IReadOnlyList<QueryRequestMessage> Messages,
    IReadOnlyList<QueryRequestTool> Tools,
    QueryRequestOutputConfig OutputConfig,
    IReadOnlyList<string> Betas,
    int? MaxTokens = null,
    QueryThinkingConfig? Thinking = null,
    string? PreviousResponseId = null,
    IReadOnlyList<string>? PreviousResponseItems = null);
