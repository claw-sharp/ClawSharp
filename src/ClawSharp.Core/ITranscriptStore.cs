namespace ClawSharp.Core;

public interface ITranscriptStore
{
    Task RecordTranscriptAsync(
        ConversationSession session,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<TranscriptReadResult> ReadTranscriptAsync(
        string transcriptPath,
        CancellationToken cancellationToken = default);

    Task RecordSessionMetadataAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
