// TS origin: ./utils/ShellCommand.ts
namespace ClawSharp.Tasks;

public sealed class LocalShellCommand
{
    private const int SigKill = 137;
    private const int SigTerm = 143;
    private static readonly TimeSpan SizeWatchdogInterval = TimeSpan.FromSeconds(1);
    private readonly Lock _stateLock = new();
    private readonly TaskCompletionSource<LocalShellExecutionResult> _resultSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _timeoutMs;
    private string? _backgroundTaskId;
    private Action? _killAction;
    private Action<Func<string, bool>>? _timeoutCallback;
    private bool _timedOut;
    private bool _killedForSize;
    private int? _forcedExitCode;
    private Timer? _sizeWatchdog;
    private bool _cleared;

    public LocalShellCommand(TaskOutput taskOutput, int timeoutMs)
    {
        TaskOutput = taskOutput;
        _timeoutMs = timeoutMs;
        Status = LocalShellCommandStatus.Running;
    }

    public TaskOutput TaskOutput { get; }

    public LocalShellCommandStatus Status { get; private set; }

    public Task<LocalShellExecutionResult> Result => _resultSource.Task;

    public void BindKillAction(Action killAction)
    {
        lock (_stateLock)
        {
            _killAction = killAction;
        }
    }

    public void OnTimeout(Action<Func<string, bool>> callback)
    {
        lock (_stateLock)
        {
            _timeoutCallback = callback;
        }
    }

    public bool HandleTimeout()
    {
        Action<Func<string, bool>>? callback;
        lock (_stateLock)
        {
            if (Status != LocalShellCommandStatus.Running || _timeoutCallback is null)
            {
                return false;
            }

            callback = _timeoutCallback;
            _timeoutCallback = null;
        }

        callback(Background);
        return true;
    }

    public bool Background(string backgroundTaskId)
    {
        lock (_stateLock)
        {
            if (Status != LocalShellCommandStatus.Running)
            {
                return false;
            }

            _backgroundTaskId = backgroundTaskId;
            Status = LocalShellCommandStatus.Backgrounded;
            _timeoutCallback = null;
            if (TaskOutput.StdoutToFile)
            {
                StartSizeWatchdogLocked();
            }
            else
            {
                TaskOutput.SpillToDisk();
            }

            return true;
        }
    }

    public bool Complete(int code, bool interrupted = false, string? stderrOverride = null)
    {
        LocalShellExecutionResult result;
        var deleteOutputFile = false;
        lock (_stateLock)
        {
            if (Status == LocalShellCommandStatus.Completed || _resultSource.Task.IsCompleted)
            {
                return false;
            }

            DisposeSizeWatchdogLocked();
            var finalCode = _forcedExitCode ?? code;
            Status = interrupted
                ? LocalShellCommandStatus.Killed
                : finalCode == SigKill
                ? LocalShellCommandStatus.Killed
                : LocalShellCommandStatus.Completed;
            var stdout = TaskOutput.GetStdoutAsync().GetAwaiter().GetResult();
            string? outputFilePath = null;
            long? outputFileSize = null;
            string? outputTaskId = null;

            if (TaskOutput.StdoutToFile && _backgroundTaskId is null)
            {
                if (TaskOutput.OutputFileRedundant)
                {
                    deleteOutputFile = true;
                }
                else
                {
                    outputFilePath = TaskOutput.Path;
                    outputFileSize = TaskOutput.OutputFileSize;
                    outputTaskId = TaskOutput.TaskId;
                }
            }

            var finalInterrupted = finalCode == SigKill;
            var finalStderr = stderrOverride ?? TaskOutput.GetStderr();
            if (_killedForSize)
            {
                finalStderr = PrependStderr(
                    $"Background command killed: output file exceeded {DiskTaskOutputStore.MaxTaskOutputBytesDisplay}",
                    finalStderr);
            }
            else if (_timedOut)
            {
                finalStderr = PrependStderr(
                    $"Command timed out after {FormatDuration(_timeoutMs)}",
                    finalStderr);
            }

            result = new LocalShellExecutionResult(
                stdout,
                finalStderr,
                finalCode,
                finalInterrupted,
                _backgroundTaskId,
                OutputFilePath: outputFilePath,
                OutputFileSize: outputFileSize,
                OutputTaskId: outputTaskId);
        }

        if (deleteOutputFile)
        {
            _ = TaskOutput.DeleteOutputFileAsync();
        }

        return _resultSource.TrySetResult(result);
    }

    public void Kill()
    {
        Action? killAction;
        var shouldCompleteInline = false;
        lock (_stateLock)
        {
            if (Status is LocalShellCommandStatus.Completed or LocalShellCommandStatus.Killed)
            {
                return;
            }

            _forcedExitCode = SigKill;
            killAction = _killAction;
            if (killAction is null)
            {
                shouldCompleteInline = true;
            }
            else
            {
                Status = LocalShellCommandStatus.Killed;
            }
        }

        if (!shouldCompleteInline)
        {
            killAction?.Invoke();
            return;
        }

        Complete(SigKill, interrupted: true);
    }

    public void MarkTimedOut()
    {
        lock (_stateLock)
        {
            _timedOut = true;
            _forcedExitCode = SigTerm;
            _timeoutCallback = null;
        }
    }

    public void MarkKilledForSize()
    {
        lock (_stateLock)
        {
            _killedForSize = true;
            _forcedExitCode = SigKill;
        }
    }

    public void Cleanup()
    {
        lock (_stateLock)
        {
            if (_cleared)
            {
                return;
            }

            _cleared = true;
            DisposeSizeWatchdogLocked();
        }

        TaskOutput.Clear();
    }

    private void StartSizeWatchdogLocked()
    {
        _sizeWatchdog?.Dispose();
        _sizeWatchdog = new Timer(
            _ =>
            {
                try
                {
                    var fileInfo = new FileInfo(TaskOutput.Path);
                    if (!fileInfo.Exists || fileInfo.Length <= DiskTaskOutputStore.MaxTaskOutputBytes)
                    {
                        return;
                    }

                    MarkKilledForSize();
                    Kill();
                }
                catch
                {
                }
            },
            state: null,
            SizeWatchdogInterval,
            SizeWatchdogInterval);
    }

    private void DisposeSizeWatchdogLocked()
    {
        _sizeWatchdog?.Dispose();
        _sizeWatchdog = null;
    }

    private static string PrependStderr(string prefix, string stderr)
    {
        return string.IsNullOrWhiteSpace(stderr)
            ? prefix
            : prefix + Environment.NewLine + stderr;
    }

    private static string FormatDuration(int timeoutMs)
    {
        if (timeoutMs % 60_000 == 0)
        {
            return $"{timeoutMs / 60_000}m";
        }

        if (timeoutMs % 1_000 == 0)
        {
            return $"{timeoutMs / 1_000}s";
        }

        return $"{timeoutMs}ms";
    }
}
