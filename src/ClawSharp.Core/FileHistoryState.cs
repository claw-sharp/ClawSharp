namespace ClawSharp.Core;

public sealed record FileHistoryState(
    IReadOnlyList<FileHistorySnapshot> Snapshots,
    IReadOnlySet<string> TrackedFiles,
    int SnapshotSequence)
{
    public static FileHistoryState Empty { get; } =
        new FileHistoryState(
            Array.Empty<FileHistorySnapshot>(),
            new HashSet<string>(StringComparer.Ordinal),
            0);

    public FileHistoryState NormalizeTrackingPaths(string projectDirectory)
    {
        var normalizedSnapshots = new List<FileHistorySnapshot>(Snapshots.Count);
        var normalizedTrackedFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var snapshot in Snapshots)
        {
            var normalizedBackups = new Dictionary<string, FileHistoryBackup>(StringComparer.Ordinal);
            foreach (var (path, backup) in snapshot.TrackedFileBackups)
            {
                var normalizedPath = NormalizeTrackingPath(projectDirectory, path);
                normalizedBackups[normalizedPath] = backup;
                normalizedTrackedFiles.Add(normalizedPath);
            }

            normalizedSnapshots.Add(
                new FileHistorySnapshot(
                    snapshot.MessageId,
                    normalizedBackups,
                    snapshot.Timestamp));
        }

        if (normalizedSnapshots.Count == 0)
        {
            return Empty;
        }

        return new FileHistoryState(
            normalizedSnapshots,
            normalizedTrackedFiles,
            SnapshotSequence == 0 ? normalizedSnapshots.Count : SnapshotSequence);
    }

    private static string NormalizeTrackingPath(string projectDirectory, string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path;
        }

        var absoluteProjectDirectory = Path.GetFullPath(projectDirectory);
        var absolutePath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolutePath.StartsWith(absoluteProjectDirectory, comparison))
        {
            return absolutePath;
        }

        return Path.GetRelativePath(absoluteProjectDirectory, absolutePath);
    }
}
