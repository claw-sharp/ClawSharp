using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class MacOsPreventSleepServiceTests
{
    [Fact]
    public void StartPreventSleep_OnMac_StartsCaffeinate_And_RestartTimer()
    {
        var runtime = new FakePreventSleepRuntime(isMacOs: true);
        var launcher = new FakePreventSleepProcessLauncher();
        var timers = new FakePreventSleepTimerFactory();
        var service = new MacOsPreventSleepService(runtime, launcher, timers);

        service.StartPreventSleep();

        Assert.Single(launcher.StartCalls);
        Assert.Single(runtime.CleanupCallbacks);
        Assert.Single(timers.CreatedTimers);
        Assert.False(launcher.Processes[0].Killed);
    }

    [Fact]
    public void StartPreventSleep_UsesReferenceCounting_Before_Killing_Caffeinate()
    {
        var runtime = new FakePreventSleepRuntime(isMacOs: true);
        var launcher = new FakePreventSleepProcessLauncher();
        var timers = new FakePreventSleepTimerFactory();
        var service = new MacOsPreventSleepService(runtime, launcher, timers);

        service.StartPreventSleep();
        service.StartPreventSleep();
        service.StopPreventSleep();

        Assert.Single(launcher.StartCalls);
        Assert.False(launcher.Processes[0].Killed);
        Assert.False(timers.CreatedTimers[0].Disposed);

        service.StopPreventSleep();

        Assert.True(launcher.Processes[0].Killed);
        Assert.True(timers.CreatedTimers[0].Disposed);
    }

    [Fact]
    public void RestartTimer_Restarts_Caffeinate_While_Active()
    {
        var runtime = new FakePreventSleepRuntime(isMacOs: true);
        var launcher = new FakePreventSleepProcessLauncher();
        var timers = new FakePreventSleepTimerFactory();
        var service = new MacOsPreventSleepService(runtime, launcher, timers);

        service.StartPreventSleep();
        timers.CreatedTimers[0].Fire();

        Assert.Equal(2, launcher.StartCalls.Count);
        Assert.True(launcher.Processes[0].Killed);
        Assert.False(launcher.Processes[1].Killed);
    }

    [Fact]
    public void StartPreventSleep_OnNonMac_DoesNotSpawn_Caffeinate()
    {
        var runtime = new FakePreventSleepRuntime(isMacOs: false);
        var launcher = new FakePreventSleepProcessLauncher();
        var timers = new FakePreventSleepTimerFactory();
        var service = new MacOsPreventSleepService(runtime, launcher, timers);

        service.StartPreventSleep();
        service.StopPreventSleep();

        Assert.Empty(launcher.StartCalls);
        Assert.Empty(timers.CreatedTimers);
        Assert.Empty(runtime.CleanupCallbacks);
    }

    [Fact]
    public void ForceStopPreventSleep_Cleans_Up_Process_And_Timer()
    {
        var runtime = new FakePreventSleepRuntime(isMacOs: true);
        var launcher = new FakePreventSleepProcessLauncher();
        var timers = new FakePreventSleepTimerFactory();
        var service = new MacOsPreventSleepService(runtime, launcher, timers);

        service.StartPreventSleep();
        runtime.CleanupCallbacks[0]();

        Assert.True(launcher.Processes[0].Killed);
        Assert.True(timers.CreatedTimers[0].Disposed);
    }

    private sealed class FakePreventSleepRuntime(bool isMacOs) : IPreventSleepRuntime
    {
        public bool IsMacOS { get; } = isMacOs;

        public List<Action> CleanupCallbacks { get; } = [];

        public void RegisterCleanup(Action callback)
        {
            CleanupCallbacks.Add(callback);
        }
    }

    private sealed class FakePreventSleepProcessLauncher : IPreventSleepProcessLauncher
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> StartCalls { get; } = [];

        public List<FakePreventSleepProcess> Processes { get; } = [];

        public IPreventSleepProcess Start(string fileName, IReadOnlyList<string> arguments)
        {
            StartCalls.Add((fileName, arguments));
            var process = new FakePreventSleepProcess();
            Processes.Add(process);
            return process;
        }
    }

    private sealed class FakePreventSleepProcess : IPreventSleepProcess
    {
        public bool Killed { get; private set; }

        public event Action? Exited;

        public void Kill()
        {
            Killed = true;
        }

        public void RaiseExited()
        {
            Exited?.Invoke();
        }
    }

    private sealed class FakePreventSleepTimerFactory : IPreventSleepTimerFactory
    {
        public List<FakePreventSleepTimer> CreatedTimers { get; } = [];

        public IPreventSleepTimer Create(TimeSpan dueTime, TimeSpan period, Action callback)
        {
            var timer = new FakePreventSleepTimer(dueTime, period, callback);
            CreatedTimers.Add(timer);
            return timer;
        }
    }

    private sealed class FakePreventSleepTimer(TimeSpan dueTime, TimeSpan period, Action callback) : IPreventSleepTimer
    {
        public TimeSpan DueTime { get; } = dueTime;

        public TimeSpan Period { get; } = period;

        public bool Disposed { get; private set; }

        public void Fire()
        {
            callback();
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
