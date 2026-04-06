// TS origin: ./utils/task/TaskOutput.ts
namespace ClawSharp.Tasks;

public sealed class TaskOutput
{
    private const int DefaultMaxMemory = 8 * 1024 * 1024;
    private const int MaxRecentLines = 1000;
    private const int MaxProgressLines = 100;
    private const int MaxProgressBytes = 4096;
    private const int PollIntervalMs = 1000;
    private const int ProgressTailBytes = 4096;

    private readonly Lock _stateLock = new();
    private static readonly Lock PollerLock = new();
    private static readonly Dictionary<string, TaskOutput> Registry = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, TaskOutput> ActivePolling = new(StringComparer.Ordinal);
    private static Task? _pollingTask;
    private static CancellationTokenSource? _pollingCancellationSource;

    private readonly Queue<string> _recentLines = new();
    private string _stdout = string.Empty;
    private string _stderr = string.Empty;
    private long _totalBytes;
    private int _totalLines;
    private bool _isOverflowed;
    private readonly Action<TaskOutputProgressUpdate>? _onProgress;
    private readonly int _maxMemory;
    private readonly bool _managedFileWrites;
    private bool _outputFileRedundant;
    private long _outputFileSize;

    public TaskOutput(
        string taskId,
        string path,
        Action<TaskOutputProgressUpdate>? onProgress = null,
        bool stdoutToFile = false,
        int maxMemory = DefaultMaxMemory,
        bool managedFileWrites = false)
    {
        TaskId = taskId;
        Path = path;
        _onProgress = onProgress;
        StdoutToFile = stdoutToFile;
        _maxMemory = maxMemory;
        _managedFileWrites = stdoutToFile && managedFileWrites;

        if (stdoutToFile && onProgress is not null)
        {
            lock (PollerLock)
            {
                Registry[taskId] = this;
            }
        }
    }

    public string TaskId { get; }

    public string Path { get; }

    public bool StdoutToFile { get; }

    public long TotalBytes
    {
        get
        {
            lock (_stateLock)
            {
                return _totalBytes;
            }
        }
    }

    public int TotalLines
    {
        get
        {
            lock (_stateLock)
            {
                return _totalLines;
            }
        }
    }

    public bool OutputFileRedundant
    {
        get
        {
            lock (_stateLock)
            {
                return _outputFileRedundant;
            }
        }
    }

    public long OutputFileSize
    {
        get
        {
            lock (_stateLock)
            {
                return _outputFileSize;
            }
        }
    }

    public void WriteStdout(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        WriteBuffered(data, isStderr: false);
    }

    public void WriteStderr(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        WriteBuffered(data, isStderr: true);
    }

    public void WriteMergedOutput(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        WriteBuffered(data, isStderr: false, mergedFileOutput: true);
    }

    public Task<string> GetStdoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (StdoutToFile)
        {
            return ReadStdoutFromFileAsync(cancellationToken);
        }

        lock (_stateLock)
        {
            if (_isOverflowed)
            {
                var recent = string.Join('\n', GetRecentLinesLocked(5));
                var sizeKilobytes = Math.Round(_totalBytes / 1024d);
                var notice = $"\nOutput truncated ({sizeKilobytes:0}KB total). Full output saved to: {Path}";
                return Task.FromResult(string.IsNullOrEmpty(recent) ? notice.TrimStart() : recent + notice);
            }

            return Task.FromResult(_stdout);
        }
    }

    public string GetStderr()
    {
        if (StdoutToFile)
        {
            return string.Empty;
        }

        lock (_stateLock)
        {
            if (_isOverflowed)
            {
                return string.Empty;
            }

            return _stderr;
        }
    }

    public bool IsOverflowed
    {
        get
        {
            lock (_stateLock)
            {
                return _isOverflowed;
            }
        }
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _stdout = string.Empty;
            _stderr = string.Empty;
            _totalBytes = 0;
            _totalLines = 0;
            _isOverflowed = false;
            _outputFileRedundant = false;
            _outputFileSize = 0;
            _recentLines.Clear();
        }

        StopPolling(TaskId);
        lock (PollerLock)
        {
            Registry.Remove(TaskId);
        }
    }

    public static void StartPolling(string taskId)
    {
        lock (PollerLock)
        {
            if (!Registry.TryGetValue(taskId, out var instance) || instance._onProgress is null)
            {
                return;
            }

            ActivePolling[taskId] = instance;
            if (_pollingTask is not null && !_pollingTask.IsCompleted)
            {
                return;
            }

            _pollingCancellationSource = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollLoopAsync(_pollingCancellationSource.Token));
        }
    }

    public static void StopPolling(string taskId)
    {
        lock (PollerLock)
        {
            ActivePolling.Remove(taskId);
            if (ActivePolling.Count == 0 && _pollingCancellationSource is not null)
            {
                _pollingCancellationSource.Cancel();
                _pollingCancellationSource = null;
                _pollingTask = null;
            }
        }
    }

    private TaskOutputProgressUpdate? UpdateProgressLocked(string data)
    {
        _totalBytes += data.Length;

        var lineCount = 0;
        var lines = new List<string>();
        var extractedBytes = 0;
        var position = data.Length;

        while (position > 0)
        {
            var previous = data.LastIndexOf('\n', position - 1);
            if (previous < 0)
            {
                break;
            }

            lineCount++;
            if (lines.Count < MaxProgressLines && extractedBytes < MaxProgressBytes)
            {
                var lineLength = position - previous - 1;
                if (lineLength > 0 && lineLength <= MaxProgressBytes - extractedBytes)
                {
                    var line = data.Substring(previous + 1, lineLength);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line);
                        extractedBytes += lineLength;
                    }
                }
            }

            position = previous;
        }

        _totalLines += lineCount;

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            _recentLines.Enqueue(lines[index]);
            while (_recentLines.Count > MaxRecentLines)
            {
                _recentLines.Dequeue();
            }
        }

        if (_onProgress is null || lines.Count == 0)
        {
            return null;
        }

        return new TaskOutputProgressUpdate(
            LastLines: string.Join('\n', GetRecentLinesLocked(5)),
            AllLines: string.Join('\n', GetRecentLinesLocked(100)),
            TotalLines: _totalLines,
            TotalBytes: _totalBytes,
            IsIncomplete: false);
    }

    private IReadOnlyList<string> GetRecentLinesLocked(int count)
    {
        if (_recentLines.Count == 0)
        {
            return [];
        }

        var allLines = _recentLines.ToArray();
        return allLines.Skip(Math.Max(0, allLines.Length - count)).ToArray();
    }

    private void ReportProgress(TaskOutputProgressUpdate? progressUpdate)
    {
        if (progressUpdate is not null)
        {
            _onProgress?.Invoke(progressUpdate);
        }
    }

    public void SpillToDisk()
    {
        lock (_stateLock)
        {
            if (!_isOverflowed)
            {
                SpillToDiskLocked(null, null);
            }
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DeleteOutputFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private void WriteBuffered(string data, bool isStderr, bool mergedFileOutput = false)
    {
        TaskOutputProgressUpdate? progressUpdate = null;
        lock (_stateLock)
        {
            if (_managedFileWrites)
            {
                AppendToOutputFile(data);
            }
            else
            {
                progressUpdate = UpdateProgressLocked(data);

                if (_isOverflowed)
                {
                    AppendToDisk(GetPersistedChunk(data, isStderr));
                }
                else
                {
                    var totalMemory = _stdout.Length + _stderr.Length + data.Length;
                    if (!StdoutToFile && totalMemory > _maxMemory)
                    {
                        SpillToDiskLocked(
                            isStderr ? data : null,
                            isStderr ? null : data);
                    }
                    else if (isStderr && !mergedFileOutput)
                    {
                        _stderr += data;
                    }
                    else
                    {
                        _stdout += data;
                    }
                }
            }
        }

        ReportProgress(progressUpdate);
    }

    private void SpillToDiskLocked(string? stderrChunk, string? stdoutChunk)
    {
        _isOverflowed = true;

        if (!string.IsNullOrEmpty(_stdout))
        {
            AppendToDisk(_stdout);
            _stdout = string.Empty;
        }

        if (!string.IsNullOrEmpty(_stderr))
        {
            AppendToDisk(GetPersistedChunk(_stderr, isStderr: true));
            _stderr = string.Empty;
        }

        if (!string.IsNullOrEmpty(stdoutChunk))
        {
            AppendToDisk(stdoutChunk);
        }

        if (!string.IsNullOrEmpty(stderrChunk))
        {
            AppendToDisk(GetPersistedChunk(stderrChunk, isStderr: true));
        }
    }

    private static string GetPersistedChunk(string data, bool isStderr)
    {
        return isStderr ? $"[stderr] {data}" : data;
    }

    private void AppendToDisk(string data)
    {
        if (StdoutToFile)
        {
            return;
        }

        AppendToOutputFile(data);
    }

    private void AppendToOutputFile(string data)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(Path, data);
    }

    private async Task<string> ReadStdoutFromFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            var bytesTotal = stream.Length;
            var bytesToRead = (int)Math.Min(bytesTotal, DiskTaskOutputStore.DefaultMaxReadBytes);
            if (bytesToRead == 0)
            {
                lock (_stateLock)
                {
                    _outputFileSize = bytesTotal;
                    _outputFileRedundant = true;
                }

                return string.Empty;
            }

            var buffer = new byte[bytesToRead];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

            lock (_stateLock)
            {
                _outputFileSize = bytesTotal;
                _outputFileRedundant = bytesTotal <= bytesRead;
            }

            return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            var code = GetReadErrorCode(ex);
            return $"<bash output unavailable: output file {Path} could not be read ({code}). This usually means another Claude Code process in the same project deleted it during startup cleanup.>";
        }
    }

    private static string GetReadErrorCode(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "ENOENT",
            DirectoryNotFoundException => "ENOENT",
            UnauthorizedAccessException => "EACCES",
            _ => "unknown"
        };
    }

    private static async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await PollActiveEntriesAsync(cancellationToken);
                await Task.Delay(PollIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task PollActiveEntriesAsync(CancellationToken cancellationToken)
    {
        List<TaskOutput> entries;
        lock (PollerLock)
        {
            entries = ActivePolling.Values.ToList();
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await entry.PollFileProgressAsync(cancellationToken);
        }
    }

    private async Task PollFileProgressAsync(CancellationToken cancellationToken)
    {
        if (_onProgress is null)
        {
            return;
        }

        var result = await ReadTailAsync(Path, ProgressTailBytes, cancellationToken);
        if (result is null)
        {
            return;
        }

        var (content, bytesRead, bytesTotal) = result.Value;
        TaskOutputProgressUpdate progressUpdate;

        lock (_stateLock)
        {
            _totalBytes = bytesTotal;
            if (string.IsNullOrEmpty(content))
            {
                progressUpdate = new TaskOutputProgressUpdate(
                    string.Empty,
                    string.Empty,
                    _totalLines,
                    bytesTotal,
                    false);
            }
            else
            {
                var position = content.Length;
                var lastFiveStart = 0;
                var lastHundredStart = 0;
                var lineCount = 0;

                while (position > 0)
                {
                    position = content.LastIndexOf('\n', position - 1);
                    lineCount++;
                    if (lineCount == 5)
                    {
                        lastFiveStart = position <= 0 ? 0 : position + 1;
                    }

                    if (lineCount == 100)
                    {
                        lastHundredStart = position <= 0 ? 0 : position + 1;
                    }
                }

                _totalLines = bytesRead >= bytesTotal
                    ? lineCount
                    : Math.Max(_totalLines, (int)Math.Round((double)bytesTotal / bytesRead * lineCount));

                progressUpdate = new TaskOutputProgressUpdate(
                    content[lastFiveStart..],
                    content[lastHundredStart..],
                    _totalLines,
                    bytesTotal,
                    bytesRead < bytesTotal);
            }
        }

        _onProgress(progressUpdate);
    }

    private static async Task<(string Content, int BytesRead, long BytesTotal)?> ReadTailAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            var bytesTotal = stream.Length;
            var bytesToRead = (int)Math.Min(bytesTotal, maxBytes);
            if (bytesToRead == 0)
            {
                return (string.Empty, 0, bytesTotal);
            }

            stream.Seek(Math.Max(0, bytesTotal - bytesToRead), SeekOrigin.Begin);
            var buffer = new byte[bytesToRead];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return (content, bytesRead, bytesTotal);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
