// TS origin: ./QueryEngine.ts, ./utils/sessionStorage.ts
namespace ClawSharp.Core;

public interface ISessionFactory
{
    ConversationSession Create();

    Task<ConversationSession?> ResumeAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<ConversationSession?> ResumeAsync(
        SessionLog sessionLog,
        CancellationToken cancellationToken = default);

    Task<ConversationSession?> ContinueMostRecentAsync(
        CancellationToken cancellationToken = default);
}
