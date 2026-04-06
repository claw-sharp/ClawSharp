// TS origin: ./services/preventSleep.ts
// TS parity status: ports the macOS `caffeinate` refcount and restart behavior on the approved C# fallback path; cleanup uses ProcessExit/CancelKeyPress and the current terminal turn boundary because the C# REPL does not yet expose the finer-grained TS waiting-vs-working state split.
using ClawSharp.Core;
using System.Diagnostics;
using System.Globalization;

namespace ClawSharp.Ui.Terminal;

public interface IPreventSleepService
{
    void StartPreventSleep();

    void StopPreventSleep();

    void ForceStopPreventSleep();
}

public sealed class MacOsPreventSleepService : IPreventSleepService
{
    private const int CaffeinateTimeoutSeconds = 300;
    private static readonly TimeSpan RestartInterval = TimeSpan.FromMinutes(4);

    private readonly object _lock = new();
    private readonly IPreventSleepRuntime _runtime;
    private readonly IPreventSleepProcessLauncher _processLauncher;
    private readonly IPreventSleepTimerFactory _timerFactory;

    private IPreventSleepProcess? _caffeinateProcess;
    private IPreventSleepTimer? _restartTimer;
    private int _refCount;
    private bool _cleanupRegistered;

    public MacOsPreventSleepService()
        : this(
            new PreventSleepRuntime(),
            new PreventSleepProcessLauncher(),
            new PreventSleepTimerFactory())
    {
    }

    public MacOsPreventSleepService(
        IPreventSleepRuntime runtime,
        IPreventSleepProcessLauncher processLauncher,
        IPreventSleepTimerFactory timerFactory)
    {
        _runtime = runtime;
        _processLauncher = processLauncher;
        _timerFactory = timerFactory;
    }

    public void StartPreventSleep()
    {
        lock (_lock)
        {
            _refCount++;
            if (_refCount != 1)
            {
                return;
            }

            SpawnCaffeinateNoLock();
            StartRestartTimerNoLock();
        }
    }

    public void StopPreventSleep()
    {
        lock (_lock)
        {
            if (_refCount > 0)
            {
                _refCount--;
            }

            if (_refCount != 0)
            {
                return;
            }

            StopRestartTimerNoLock();
            KillCaffeinateNoLock();
        }
    }

    public void ForceStopPreventSleep()
    {
        lock (_lock)
        {
            _refCount = 0;
            StopRestartTimerNoLock();
            KillCaffeinateNoLock();
        }
    }

    private void StartRestartTimerNoLock()
    {
        if (!_runtime.IsMacOS || _restartTimer is not null)
        {
            return;
        }

        _restartTimer = _timerFactory.Create(
            RestartInterval,
            RestartInterval,
            RestartCaffeinate);
    }

    private void StopRestartTimerNoLock()
    {
        _restartTimer?.Dispose();
        _restartTimer = null;
    }

    private void RestartCaffeinate()
    {
        lock (_lock)
        {
            if (_refCount <= 0)
            {
                return;
            }

            ClawSharpTelemetry.LogDebug("Restarting caffeinate to maintain sleep prevention");
            KillCaffeinateNoLock();
            SpawnCaffeinateNoLock();
        }
    }

    private void SpawnCaffeinateNoLock()
    {
        if (!_runtime.IsMacOS || _caffeinateProcess is not null)
        {
            return;
        }

        RegisterCleanupNoLock();

        try
        {
            var process = _processLauncher.Start(
                "caffeinate",
                ["-i", "-t", CaffeinateTimeoutSeconds.ToString(CultureInfo.InvariantCulture)]);
            _caffeinateProcess = process;
            process.Exited += () => OnProcessExited(process);
            ClawSharpTelemetry.LogDebug("Started caffeinate to prevent sleep");
        }
        catch (Exception exception)
        {
            ClawSharpTelemetry.LogDebug($"caffeinate spawn error: {exception.Message}");
            _caffeinateProcess = null;
        }
    }

    private void OnProcessExited(IPreventSleepProcess process)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_caffeinateProcess, process))
            {
                _caffeinateProcess = null;
            }
        }
    }

    private void RegisterCleanupNoLock()
    {
        if (_cleanupRegistered)
        {
            return;
        }

        _cleanupRegistered = true;
        _runtime.RegisterCleanup(ForceStopPreventSleep);
    }

    private void KillCaffeinateNoLock()
    {
        if (_caffeinateProcess is null)
        {
            return;
        }

        var process = _caffeinateProcess;
        _caffeinateProcess = null;

        try
        {
            process.Kill();
            ClawSharpTelemetry.LogDebug("Stopped caffeinate, allowing sleep");
        }
        catch
        {
            // Process may have already exited.
        }
    }
}

public interface IPreventSleepRuntime
{
    bool IsMacOS { get; }

    void RegisterCleanup(Action callback);
}

public sealed class PreventSleepRuntime : IPreventSleepRuntime
{
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public void RegisterCleanup(Action callback)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => callback();
        Console.CancelKeyPress += (_, _) => callback();
    }
}

public interface IPreventSleepProcessLauncher
{
    IPreventSleepProcess Start(string fileName, IReadOnlyList<string> arguments);
}

public sealed class PreventSleepProcessLauncher : IPreventSleepProcessLauncher
{
    public IPreventSleepProcess Start(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {fileName}.");
        }

        return new PreventSleepProcess(process);
    }
}

public interface IPreventSleepProcess
{
    event Action? Exited;

    void Kill();
}

public sealed class PreventSleepProcess : IPreventSleepProcess
{
    private readonly Process _process;

    public PreventSleepProcess(Process process)
    {
        _process = process;
        _process.Exited += (_, _) => Exited?.Invoke();
    }

    public event Action? Exited;

    public void Kill()
    {
        if (_process.HasExited)
        {
            return;
        }

        _process.Kill(entireProcessTree: false);
    }
}

public interface IPreventSleepTimerFactory
{
    IPreventSleepTimer Create(TimeSpan dueTime, TimeSpan period, Action callback);
}

public sealed class PreventSleepTimerFactory : IPreventSleepTimerFactory
{
    public IPreventSleepTimer Create(TimeSpan dueTime, TimeSpan period, Action callback)
    {
        return new PreventSleepTimer(dueTime, period, callback);
    }
}

public interface IPreventSleepTimer : IDisposable
{
}

public sealed class PreventSleepTimer : IPreventSleepTimer
{
    private readonly Timer _timer;

    public PreventSleepTimer(TimeSpan dueTime, TimeSpan period, Action callback)
    {
        _timer = new Timer(
            _ => callback(),
            null,
            dueTime,
            period);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
