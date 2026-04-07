using System.Diagnostics;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class StartupProfiler
{
    private const double StatsigSampleRate = 0.005d;
    private static readonly IReadOnlyDictionary<string, (string Start, string End)> PhaseDefinitions =
        new Dictionary<string, (string Start, string End)>(StringComparer.Ordinal)
        {
            ["import_time"] = ("cli_entry", "main_tsx_imports_loaded"),
            ["init_time"] = ("init_function_start", "init_function_end"),
            ["settings_time"] = ("eagerLoadSettings_start", "eagerLoadSettings_end"),
            ["total_time"] = ("cli_entry", "main_after_run")
        };
    private static readonly Lock SyncRoot = new();
    private static bool _initialized;
    private static bool _detailedProfilingEnabled;
    private static bool _statsigLoggingSampled;
    private static bool _shouldProfile;
    private static bool _reported;
    private static string _runId = Guid.NewGuid().ToString("N");
    private static Stopwatch _stopwatch = Stopwatch.StartNew();
    private static List<StartupCheckpoint> _checkpoints = [];

    public static void Checkpoint(string name)
    {
        EnsureInitialized();
        if (!_shouldProfile)
        {
            return;
        }

        lock (SyncRoot)
        {
            _checkpoints.Add(new StartupCheckpoint(
                name,
                _stopwatch.Elapsed.TotalMilliseconds,
                _detailedProfilingEnabled ? CaptureMemorySnapshot() : null));
        }
    }

    public static bool IsDetailedProfilingEnabled()
    {
        EnsureInitialized();
        return _detailedProfilingEnabled;
    }

    public static string GetStartupPerfLogPath(string? sessionId = null)
    {
        EnsureInitialized();
        return ClaudeConfigPaths.GetStartupPerfLogPath(string.IsNullOrWhiteSpace(sessionId) ? _runId : sessionId);
    }

    public static void Report(string? sessionId = null)
    {
        EnsureInitialized();

        lock (SyncRoot)
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            LogStartupPerf();

            if (!_detailedProfilingEnabled)
            {
                return;
            }

            var path = GetStartupPerfLogPath(sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var report = GetReport();
            File.WriteAllText(path, report);
            ClawSharpTelemetry.LogDebug("Startup profiling report:", DebugLogLevel.Info);
            ClawSharpTelemetry.LogDebug(report, DebugLogLevel.Info);
        }
    }

    public static void ResetForTesting()
    {
        lock (SyncRoot)
        {
            _initialized = false;
            _detailedProfilingEnabled = false;
            _statsigLoggingSampled = false;
            _shouldProfile = false;
            _reported = false;
            _runId = Guid.NewGuid().ToString("N");
            _stopwatch = Stopwatch.StartNew();
            _checkpoints = [];
        }
    }

    private static void EnsureInitialized()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            _detailedProfilingEnabled = IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP"));
            _statsigLoggingSampled =
                string.Equals(Environment.GetEnvironmentVariable("USER_TYPE"), "ant", StringComparison.Ordinal) ||
                Random.Shared.NextDouble() < StatsigSampleRate;
            _shouldProfile = _detailedProfilingEnabled || _statsigLoggingSampled;
            _initialized = true;

            if (_shouldProfile)
            {
                _checkpoints.Add(new StartupCheckpoint(
                    "profiler_initialized",
                    _stopwatch.Elapsed.TotalMilliseconds,
                    _detailedProfilingEnabled ? CaptureMemorySnapshot() : null));
            }
        }
    }

    private static void LogStartupPerf()
    {
        if (!_statsigLoggingSampled || _checkpoints.Count == 0)
        {
            return;
        }

        var checkpointLookup = _checkpoints.ToDictionary(
            static checkpoint => checkpoint.Name,
            static checkpoint => checkpoint.TotalMilliseconds,
            StringComparer.Ordinal);
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var phase in PhaseDefinitions)
        {
            if (checkpointLookup.TryGetValue(phase.Value.Start, out var start) &&
                checkpointLookup.TryGetValue(phase.Value.End, out var end))
            {
                metadata[$"{phase.Key}_ms"] = Math.Round(end - start);
            }
        }

        metadata["checkpoint_count"] = _checkpoints.Count;
        ClawSharpTelemetry.LogEvent("tengu_startup_perf", metadata);
        ClawSharpTelemetry.RecordMetric("startup.checkpoint_count", _checkpoints.Count);
    }

    private static string GetReport()
    {
        if (!_detailedProfilingEnabled)
        {
            return "Startup profiling not enabled";
        }

        if (_checkpoints.Count == 0)
        {
            return "No profiling checkpoints recorded";
        }

        var lines = new List<string>
        {
            new string('=', 80),
            "STARTUP PROFILING REPORT",
            new string('=', 80),
            string.Empty
        };

        double previous = 0;
        foreach (var checkpoint in _checkpoints)
        {
            lines.Add(FormatTimelineLine(
                checkpoint.TotalMilliseconds,
                checkpoint.TotalMilliseconds - previous,
                checkpoint.Name,
                checkpoint.MemorySnapshot,
                8,
                7));
            previous = checkpoint.TotalMilliseconds;
        }

        lines.Add(string.Empty);
        lines.Add($"Total startup time: {FormatMs(_checkpoints[^1].TotalMilliseconds)}ms");
        lines.Add(new string('=', 80));
        return string.Join(Environment.NewLine, lines);
    }

    private static MemorySnapshot CaptureMemorySnapshot()
    {
        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(process.WorkingSet64, GC.GetTotalMemory(forceFullCollection: false));
    }

    private static string FormatTimelineLine(
        double totalMilliseconds,
        double deltaMilliseconds,
        string name,
        MemorySnapshot? memory,
        int totalPad,
        int deltaPad)
    {
        var memoryInfo = memory is null
            ? string.Empty
            : $" | RSS: {FormatFileSize(memory.WorkingSetBytes)}, Heap: {FormatFileSize(memory.ManagedHeapBytes)}";
        return $"[+{FormatMs(totalMilliseconds).PadLeft(totalPad)}ms] (+{FormatMs(deltaMilliseconds).PadLeft(deltaPad)}ms) {name}{memoryInfo}";
    }

    private static string FormatMs(double milliseconds)
    {
        return milliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private sealed record StartupCheckpoint(
        string Name,
        double TotalMilliseconds,
        MemorySnapshot? MemorySnapshot);

    private sealed record MemorySnapshot(
        long WorkingSetBytes,
        long ManagedHeapBytes);
}
