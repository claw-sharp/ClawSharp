// TS origin: ./utils/fileHistory.ts, ./utils/sessionStorage.ts
using System.Security.Cryptography;
using System.Text;
using DiffPlex;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public static class FileHistoryService
{
    private const int MaxSnapshots = 100;

    public static async Task MakeSnapshotAsync(
        ConversationSession session,
        ClawSharpSettings settings,
        string messageId,
        IFileUpdateNotifier? fileUpdateNotifier = null,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Runtime.FileCheckpointingEnabled || string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        var captured = session.FileHistoryState;
        var trackedFileBackups = new Dictionary<string, FileHistoryBackup>(StringComparer.Ordinal);
        var mostRecentSnapshot = captured.Snapshots.LastOrDefault();
        if (mostRecentSnapshot is not null)
        {
            foreach (var trackingPath in captured.TrackedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = ExpandTrackedFilePath(session.ProjectDirectory, trackingPath);
                mostRecentSnapshot.TrackedFileBackups.TryGetValue(trackingPath, out var latestBackup);
                var nextVersion = latestBackup is null ? 1 : latestBackup.Version + 1;

                if (!File.Exists(filePath))
                {
                    trackedFileBackups[trackingPath] = new FileHistoryBackup(
                        null,
                        nextVersion,
                        DateTimeOffset.UtcNow);
                    continue;
                }

                if (latestBackup is not null &&
                    latestBackup.BackupFileName is not null &&
                    !await HasOriginFileChangedAsync(session.Id, filePath, latestBackup.BackupFileName, cancellationToken))
                {
                    trackedFileBackups[trackingPath] = latestBackup;
                    continue;
                }

                trackedFileBackups[trackingPath] = await CreateBackupAsync(
                    session.Id,
                    filePath,
                    nextVersion,
                    cancellationToken);
            }
        }

        var lastSnapshot = session.FileHistoryState.Snapshots.LastOrDefault();
        if (lastSnapshot is not null)
        {
            foreach (var trackingPath in session.FileHistoryState.TrackedFiles)
            {
                if (trackedFileBackups.ContainsKey(trackingPath))
                {
                    continue;
                }

                if (lastSnapshot.TrackedFileBackups.TryGetValue(trackingPath, out var inheritedBackup))
                {
                    trackedFileBackups[trackingPath] = inheritedBackup;
                }
            }
        }

        var snapshot = new FileHistorySnapshot(
            messageId,
            trackedFileBackups,
            DateTimeOffset.UtcNow);
        await NotifySnapshotFilesUpdatedAsync(
            session,
            session.FileHistoryState,
            snapshot,
            fileUpdateNotifier ?? new NullFileUpdateNotifier(),
            cancellationToken);
        session.AppendFileHistorySnapshot(snapshot, MaxSnapshots);
    }

    public static async Task TrackEditAsync(
        ToolExecutionContext context,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!context.Settings.Runtime.FileCheckpointingEnabled)
        {
            return;
        }

        var messageId = TryGetActiveToolUseMessageId(context.Session);
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        var trackingPath = MaybeShortenFilePath(context.Session.ProjectDirectory, filePath);
        context.Session.EnsureFileHistorySnapshot(messageId);
        var currentSnapshot = context.Session.FileHistoryState.Snapshots.LastOrDefault();

        if (currentSnapshot is not null &&
            currentSnapshot.TrackedFileBackups.ContainsKey(trackingPath))
        {
            return;
        }

        var nextVersion = GetLatestVersion(context.Session.FileHistoryState, trackingPath) + 1;
        var backup = await CreateBackupAsync(context.Session.Id, filePath, nextVersion, cancellationToken);
        context.Session.TrackFileHistoryBackup(messageId, trackingPath, backup);
    }

    public static bool CanRestore(
        ConversationSession session,
        ClawSharpSettings settings,
        string messageId)
    {
        return settings.Runtime.FileCheckpointingEnabled &&
               session.FileHistoryState.Snapshots.Any(snapshot => string.Equals(snapshot.MessageId, messageId, StringComparison.Ordinal));
    }

    public static async Task<FileHistoryDiffStats?> GetDiffStatsAsync(
        ConversationSession session,
        ClawSharpSettings settings,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Runtime.FileCheckpointingEnabled)
        {
            return null;
        }

        var targetSnapshot = session.FileHistoryState.Snapshots.LastOrDefault(
            snapshot => string.Equals(snapshot.MessageId, messageId, StringComparison.Ordinal));
        if (targetSnapshot is null)
        {
            return null;
        }

        var filesChanged = new List<string>();
        var insertions = 0;
        var deletions = 0;

        foreach (var trackingPath in session.FileHistoryState.TrackedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ExpandTrackedFilePath(session.ProjectDirectory, trackingPath);
            var backupFileName = ResolveTargetBackupFileName(session.FileHistoryState, targetSnapshot, trackingPath);
            if (backupFileName is null && !File.Exists(filePath))
            {
                continue;
            }

            if (backupFileName is null)
            {
                filesChanged.Add(filePath);
                continue;
            }

            var stats = await ComputeDiffStatsForFileAsync(session.Id, filePath, backupFileName, cancellationToken);
            if (stats is null)
            {
                continue;
            }

            if (stats.Insertions > 0 || stats.Deletions > 0 || stats.FilesChanged.Count > 0)
            {
                filesChanged.AddRange(stats.FilesChanged);
                insertions += stats.Insertions;
                deletions += stats.Deletions;
            }
        }

        return new FileHistoryDiffStats(filesChanged, insertions, deletions);
    }

    public static async Task<bool> HasAnyChangesAsync(
        ConversationSession session,
        ClawSharpSettings settings,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Runtime.FileCheckpointingEnabled)
        {
            return false;
        }

        var targetSnapshot = session.FileHistoryState.Snapshots.LastOrDefault(
            snapshot => string.Equals(snapshot.MessageId, messageId, StringComparison.Ordinal));
        if (targetSnapshot is null)
        {
            return false;
        }

        foreach (var trackingPath in session.FileHistoryState.TrackedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ExpandTrackedFilePath(session.ProjectDirectory, trackingPath);
            var backupFileName = ResolveTargetBackupFileName(session.FileHistoryState, targetSnapshot, trackingPath);
            if (backupFileName is null)
            {
                if (File.Exists(filePath))
                {
                    return true;
                }

                continue;
            }

            if (await HasOriginFileChangedAsync(session.Id, filePath, backupFileName, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<IReadOnlyList<string>> RewindAsync(
        ConversationSession session,
        ClawSharpSettings settings,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Runtime.FileCheckpointingEnabled)
        {
            return [];
        }

        var targetSnapshot = session.FileHistoryState.Snapshots.LastOrDefault(
            snapshot => string.Equals(snapshot.MessageId, messageId, StringComparison.Ordinal));
        if (targetSnapshot is null)
        {
            throw new InvalidOperationException("The selected snapshot was not found.");
        }

        var filesChanged = new List<string>();
        foreach (var trackingPath in session.FileHistoryState.TrackedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ExpandTrackedFilePath(session.ProjectDirectory, trackingPath);
            var backupFileName = ResolveTargetBackupFileName(session.FileHistoryState, targetSnapshot, trackingPath);

            if (backupFileName is null)
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    filesChanged.Add(filePath);
                }

                continue;
            }

            if (await HasOriginFileChangedAsync(session.Id, filePath, backupFileName, cancellationToken))
            {
                await RestoreBackupAsync(session.Id, filePath, backupFileName, cancellationToken);
                filesChanged.Add(filePath);
            }
        }

        return filesChanged;
    }

    private static int GetLatestVersion(FileHistoryState state, string trackingPath)
    {
        var latestVersion = 0;
        foreach (var snapshot in state.Snapshots)
        {
            if (snapshot.TrackedFileBackups.TryGetValue(trackingPath, out var backup))
            {
                latestVersion = Math.Max(latestVersion, backup.Version);
            }
        }

        return latestVersion;
    }

    private static string? ResolveTargetBackupFileName(
        FileHistoryState state,
        FileHistorySnapshot targetSnapshot,
        string trackingPath)
    {
        if (targetSnapshot.TrackedFileBackups.TryGetValue(trackingPath, out var backup))
        {
            return backup.BackupFileName;
        }

        foreach (var snapshot in state.Snapshots)
        {
            if (snapshot.TrackedFileBackups.TryGetValue(trackingPath, out var firstVersionBackup) &&
                firstVersionBackup.Version == 1)
            {
                return firstVersionBackup.BackupFileName;
            }
        }

        return null;
    }

    private static async Task<FileHistoryBackup> CreateBackupAsync(
        string sessionId,
        string filePath,
        int version,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new FileHistoryBackup(null, version, DateTimeOffset.UtcNow);
        }

        var backupFileName = GetBackupFileName(filePath, version);
        var backupPath = SessionStoragePaths.GetFileHistoryBackupPath(sessionId, backupFileName);
        var backupDirectory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        await using var source = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        await using var destination = new FileStream(
            backupPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);

        return new FileHistoryBackup(backupFileName, version, DateTimeOffset.UtcNow);
    }

    private static async Task<bool> HasOriginFileChangedAsync(
        string sessionId,
        string originalFile,
        string backupFileName,
        CancellationToken cancellationToken)
    {
        var backupPath = SessionStoragePaths.GetFileHistoryBackupPath(sessionId, backupFileName);

        FileInfo? originalInfo = null;
        if (File.Exists(originalFile))
        {
            originalInfo = new FileInfo(originalFile);
        }

        FileInfo? backupInfo = null;
        if (File.Exists(backupPath))
        {
            backupInfo = new FileInfo(backupPath);
        }

        if ((originalInfo is null) != (backupInfo is null))
        {
            return true;
        }

        if (originalInfo is null || backupInfo is null)
        {
            return false;
        }

        if (originalInfo.Length != backupInfo.Length)
        {
            return true;
        }

        if (originalInfo.LastWriteTimeUtc < backupInfo.LastWriteTimeUtc)
        {
            return false;
        }

        var originalContent = await File.ReadAllTextAsync(originalFile, cancellationToken);
        var backupContent = await File.ReadAllTextAsync(backupPath, cancellationToken);
        return !string.Equals(originalContent, backupContent, StringComparison.Ordinal);
    }

    private static async Task<FileHistoryDiffStats?> ComputeDiffStatsForFileAsync(
        string sessionId,
        string originalFile,
        string backupFileName,
        CancellationToken cancellationToken)
    {
        var backupPath = SessionStoragePaths.GetFileHistoryBackupPath(sessionId, backupFileName);
        var originalContent = File.Exists(originalFile)
            ? await File.ReadAllTextAsync(originalFile, cancellationToken)
            : null;
        var backupContent = File.Exists(backupPath)
            ? await File.ReadAllTextAsync(backupPath, cancellationToken)
            : null;

        if (originalContent is null && backupContent is null)
        {
            return new FileHistoryDiffStats([], 0, 0);
        }

        var diffResult = Differ.Instance.CreateLineDiffs(originalContent ?? string.Empty, backupContent ?? string.Empty, false);
        var insertions = 0;
        var deletions = 0;
        foreach (var block in diffResult.DiffBlocks)
        {
            insertions += block.InsertCountB;
            deletions += block.DeleteCountA;
        }

        if (insertions == 0 && deletions == 0)
        {
            return new FileHistoryDiffStats([], 0, 0);
        }

        return new FileHistoryDiffStats([originalFile], insertions, deletions);
    }

    private static async Task RestoreBackupAsync(
        string sessionId,
        string filePath,
        string backupFileName,
        CancellationToken cancellationToken)
    {
        var backupPath = SessionStoragePaths.GetFileHistoryBackupPath(sessionId, backupFileName);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = new FileStream(
            backupPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            useAsync: true);
        await using var destination = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task NotifySnapshotFilesUpdatedAsync(
        ConversationSession session,
        FileHistoryState previousState,
        FileHistorySnapshot newSnapshot,
        IFileUpdateNotifier fileUpdateNotifier,
        CancellationToken cancellationToken)
    {
        var oldSnapshot = previousState.Snapshots.LastOrDefault();
        if (oldSnapshot is null)
        {
            return;
        }

        foreach (var trackingPath in previousState.TrackedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ExpandTrackedFilePath(session.ProjectDirectory, trackingPath);
            oldSnapshot.TrackedFileBackups.TryGetValue(trackingPath, out var oldBackup);
            newSnapshot.TrackedFileBackups.TryGetValue(trackingPath, out var newBackup);

            if (string.Equals(oldBackup?.BackupFileName, newBackup?.BackupFileName, StringComparison.Ordinal) &&
                oldBackup?.Version == newBackup?.Version)
            {
                continue;
            }

            var oldContent = await ReadBackupContentAsync(session.Id, oldBackup?.BackupFileName, cancellationToken);
            var newContent = await ReadBackupContentAsync(session.Id, newBackup?.BackupFileName, cancellationToken);
            if (!string.Equals(oldContent, newContent, StringComparison.Ordinal))
            {
                await fileUpdateNotifier.NotifyFileUpdatedAsync(
                    filePath,
                    oldContent,
                    newContent,
                    cancellationToken);
            }
        }
    }

    private static async Task<string?> ReadBackupContentAsync(
        string sessionId,
        string? backupFileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupFileName))
        {
            return null;
        }

        var backupPath = SessionStoragePaths.GetFileHistoryBackupPath(sessionId, backupFileName);
        if (!File.Exists(backupPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(backupPath, cancellationToken);
    }

    private static string MaybeShortenFilePath(string projectDirectory, string filePath)
    {
        var projectRoot = Path.GetFullPath(projectDirectory);
        var fullPath = Path.GetFullPath(filePath);
        if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(projectRoot, fullPath);
        }

        return fullPath;
    }

    private static string? TryGetActiveToolUseMessageId(ConversationSession session)
    {
        for (var index = session.Messages.Count - 1; index >= 0; index--)
        {
            var message = session.Messages[index];
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            if (message.ContentBlocks.Any(block => block.Kind == MessageContentKind.ToolUse))
            {
                return message.Id;
            }
        }

        return null;
    }

    private static string ExpandTrackedFilePath(string projectDirectory, string trackingPath)
    {
        return Path.IsPathRooted(trackingPath)
            ? trackingPath
            : Path.GetFullPath(Path.Combine(projectDirectory, trackingPath));
    }

    private static string GetBackupFileName(string filePath, int version)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
        return $"{Convert.ToHexStringLower(hash.AsSpan(0, 8))}@v{version}";
    }
}
