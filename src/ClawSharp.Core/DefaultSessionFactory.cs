// TS origin: ./QueryEngine.ts, ./utils/sessionStorage.ts
namespace ClawSharp.Core;

public sealed class DefaultSessionFactory : ISessionFactory
{
    private readonly string _workspaceRoot;
    private readonly ITranscriptStore? _transcriptStore;

    public DefaultSessionFactory(string workspaceRoot, ITranscriptStore? transcriptStore = null)
    {
        _workspaceRoot = workspaceRoot;
        _transcriptStore = transcriptStore;
    }

    public ConversationSession Create()
    {
        return new ConversationSession(Guid.NewGuid().ToString("N"), _workspaceRoot);
    }

    public async Task<ConversationSession?> ResumeAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_transcriptStore is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var transcriptPath = SessionStoragePaths.GetTranscriptPath(_workspaceRoot, sessionId);
        if (!File.Exists(transcriptPath))
        {
            return null;
        }

        var transcript = await _transcriptStore.ReadTranscriptAsync(transcriptPath, cancellationToken);
        return CreateResumedSession(sessionId, transcript);
    }

    public async Task<ConversationSession?> ResumeAsync(
        SessionLog sessionLog,
        CancellationToken cancellationToken = default)
    {
        if (_transcriptStore is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sessionLog.SessionId) ||
            string.IsNullOrWhiteSpace(sessionLog.TranscriptPath) ||
            !File.Exists(sessionLog.TranscriptPath))
        {
            return null;
        }

        var transcript = await _transcriptStore.ReadTranscriptAsync(sessionLog.TranscriptPath, cancellationToken);
        return CreateResumedSession(sessionLog.SessionId, transcript, sessionLog.ProjectDirectory);
    }

    public async Task<ConversationSession?> ContinueMostRecentAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transcriptStore is null)
        {
            return null;
        }

        var projectDir = SessionStoragePaths.GetProjectDir(_workspaceRoot);
        if (!Directory.Exists(projectDir))
        {
            return null;
        }

        var transcriptPath = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Where(IsSessionTranscriptPath)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
        if (transcriptPath is null)
        {
            return null;
        }

        var sessionId = Path.GetFileNameWithoutExtension(transcriptPath);
        var transcript = await _transcriptStore.ReadTranscriptAsync(transcriptPath, cancellationToken);
        return CreateResumedSession(sessionId, transcript);
    }

    private ConversationSession CreateResumedSession(
        string sessionId,
        TranscriptReadResult transcript,
        string? projectDirectory = null)
    {
        var session = new ConversationSession(sessionId, projectDirectory ?? _workspaceRoot);
        foreach (var message in transcript.Messages)
        {
            session.AddRecorded(message);
        }

        session.RestoreCustomTitle(transcript.CustomTitle);
        session.RestoreFileHistoryState(transcript.FileHistoryState.NormalizeTrackingPaths(session.ProjectDirectory));

        return session;
    }

    private static bool IsSessionTranscriptPath(string path)
    {
        return Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _);
    }
}
