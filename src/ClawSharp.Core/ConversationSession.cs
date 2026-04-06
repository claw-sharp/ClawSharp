namespace ClawSharp.Core;

public sealed class ConversationSession
{
    private readonly Lock _messageLock = new();
    private readonly List<ChatMessage> _messages = [];
    private readonly HashSet<string> _recordedMessageIds = new(StringComparer.Ordinal);
    private readonly List<FileHistorySnapshotEntry> _pendingFileHistorySnapshots = [];
    private readonly string _transcriptPath;
    private string? _customTitle;
    private string? _recordedCustomTitle;
    private FileHistoryState _fileHistoryState = FileHistoryState.Empty;
    private AttributionState _attributionState;

    public ConversationSession(string id, string projectDirectory, string? transcriptPathOverride = null)
    {
        Id = id;
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        TranscriptPathOverride = string.IsNullOrWhiteSpace(transcriptPathOverride)
            ? null
            : Path.GetFullPath(transcriptPathOverride);
        _transcriptPath = TranscriptPathOverride ?? SessionStoragePaths.GetTranscriptPath(ProjectDirectory, Id);
        _attributionState = AttributionState.CreateEmpty("cli"); // Default surface
    }

    public string Id { get; }
    public string ProjectDirectory { get; }
    public string TranscriptPath => _transcriptPath;
    public string? CustomTitle => _customTitle;
    public FileHistoryState FileHistoryState => _fileHistoryState;
    public AttributionState AttributionState => _attributionState;
    public string? TranscriptPathOverride { get; }

    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (_messageLock)
            {
                return _messages.ToArray();
            }
        }
    }

    public void Add(ChatMessage message)
    {
        lock (_messageLock)
        {
            _messages.Add(message);
        }
    }

    public void AddRecorded(ChatMessage message)
    {
        lock (_messageLock)
        {
            _messages.Add(message);
            _recordedMessageIds.Add(message.Id);
        }
    }

    public bool HasRecorded(string messageId)
    {
        lock (_messageLock)
        {
            return _recordedMessageIds.Contains(messageId);
        }
    }

    public void MarkRecorded(string messageId)
    {
        lock (_messageLock)
        {
            _recordedMessageIds.Add(messageId);
        }
    }

    public void SetCustomTitle(string customTitle)
    {
        _customTitle = customTitle.Trim();
    }

    public void RestoreCustomTitle(string? customTitle)
    {
        _customTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();
        _recordedCustomTitle = _customTitle;
    }

    public bool HasUnrecordedCustomTitle()
    {
        return
            !string.IsNullOrWhiteSpace(_customTitle) &&
            !string.Equals(_customTitle, _recordedCustomTitle, StringComparison.Ordinal);
    }

    public void MarkCustomTitleRecorded()
    {
        _recordedCustomTitle = _customTitle;
    }

    public void EnsureFileHistorySnapshot(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        if (_fileHistoryState.Snapshots.Count > 0 &&
            string.Equals(_fileHistoryState.Snapshots[^1].MessageId, messageId, StringComparison.Ordinal))
        {
            return;
        }

        var snapshots = _fileHistoryState.Snapshots.ToList();
        var snapshot = new FileHistorySnapshot(
            messageId,
            new Dictionary<string, FileHistoryBackup>(StringComparer.Ordinal),
            DateTimeOffset.UtcNow);
        snapshots.Add(snapshot);
        _fileHistoryState = new FileHistoryState(
            snapshots,
            _fileHistoryState.TrackedFiles,
            _fileHistoryState.SnapshotSequence + 1);
        _pendingFileHistorySnapshots.Add(new FileHistorySnapshotEntry(snapshot, false));
    }

    public void AppendFileHistorySnapshot(FileHistorySnapshot snapshot, int maxSnapshots)
    {
        if (string.IsNullOrWhiteSpace(snapshot.MessageId))
        {
            return;
        }

        var snapshots = _fileHistoryState.Snapshots.ToList();
        snapshots.Add(snapshot);
        if (maxSnapshots > 0 && snapshots.Count > maxSnapshots)
        {
            snapshots = snapshots.Skip(snapshots.Count - maxSnapshots).ToList();
        }

        var trackedFiles = new HashSet<string>(_fileHistoryState.TrackedFiles, StringComparer.Ordinal);
        foreach (var trackingPath in snapshot.TrackedFileBackups.Keys)
        {
            trackedFiles.Add(trackingPath);
        }

        _fileHistoryState = new FileHistoryState(
            snapshots,
            trackedFiles,
            _fileHistoryState.SnapshotSequence + 1);
        _pendingFileHistorySnapshots.Add(new FileHistorySnapshotEntry(snapshot, false));
    }

    public void TrackFileHistoryBackup(string messageId, string trackingPath, FileHistoryBackup backup)
    {
        EnsureFileHistorySnapshot(messageId);

        if (_fileHistoryState.Snapshots.Count == 0)
        {
            return;
        }

        var snapshots = _fileHistoryState.Snapshots.ToList();
        var lastSnapshot = snapshots[^1];
        if (lastSnapshot.TrackedFileBackups.ContainsKey(trackingPath))
        {
            return;
        }

        var trackedBackups = new Dictionary<string, FileHistoryBackup>(lastSnapshot.TrackedFileBackups, StringComparer.Ordinal)
        {
            [trackingPath] = backup
        };

        snapshots[^1] = new FileHistorySnapshot(
            lastSnapshot.MessageId,
            trackedBackups,
            lastSnapshot.Timestamp);

        var trackedFiles = new HashSet<string>(_fileHistoryState.TrackedFiles, StringComparer.Ordinal)
        {
            trackingPath
        };

        _fileHistoryState = new FileHistoryState(
            snapshots,
            trackedFiles,
            _fileHistoryState.SnapshotSequence);
        _pendingFileHistorySnapshots.Add(new FileHistorySnapshotEntry(snapshots[^1], true));
    }

    public IReadOnlyList<FileHistorySnapshotEntry> DrainPendingFileHistorySnapshots()
    {
        var snapshotEntries = _pendingFileHistorySnapshots.ToArray();
        _pendingFileHistorySnapshots.Clear();
        return snapshotEntries;
    }

    public void RestoreFileHistoryState(FileHistoryState state)
    {
        _fileHistoryState = state;
        _pendingFileHistorySnapshots.Clear();
    }

    public void RestoreAttributionState(AttributionState state)
    {
        _attributionState = state;
    }

    public void UpdateAttributionState(AttributionState state)
    {
        _attributionState = state;
    }
}
