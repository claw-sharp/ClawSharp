namespace ClawSharp.Contracts.Threads;

public sealed record ThreadDetail(
    ThreadSummary Thread,
    IReadOnlyList<ThreadMessage> Messages,
    bool HasMoreMessages = false,
    string? NextBeforeMessageId = null);
