using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public enum DebugLogLevel
{
    Verbose,
    Debug,
    Info,
    Warn,
    Error
}

public interface IClawSharpTelemetrySpan : IDisposable
{
    void SetAttribute(string key, object? value);

    void AddEvent(string name, IReadOnlyDictionary<string, object?>? attributes = null);

    void RecordException(Exception exception);
}

public static partial class ClawSharpTelemetry
{
    private static readonly object SyncRoot = new();
    private static readonly AsyncLocal<TelemetrySpan?> InteractionContext = new();
    private static readonly AsyncLocal<TelemetrySpan?> ToolContext = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private static readonly Dictionary<DebugLogLevel, int> LevelOrder = new()
    {
        [DebugLogLevel.Verbose] = 0,
        [DebugLogLevel.Debug] = 1,
        [DebugLogLevel.Info] = 2,
        [DebugLogLevel.Warn] = 3,
        [DebugLogLevel.Error] = 4
    };
    private static readonly ActivitySource ActivitySource = new("ClawSharp.Telemetry");
    private static readonly PerfettoTracer Perfetto = new();

    private static bool _initialized;
    private static bool _hooksInstalled;
    private static bool _hasFormattedOutput;
    private static bool _runtimeDebugEnabled;
    private static string _runId = Guid.NewGuid().ToString("N");
    private static string _sessionId = _runId;
    private static string? _workspaceRoot;
    private static long _eventSequence;
    private static DebugLogLevel _minimumDebugLogLevel = DebugLogLevel.Debug;
    private static string? _debugFilter;

    public static void Initialize(string workspaceRoot, string? sessionId = null)
    {
        lock (SyncRoot)
        {
            _workspaceRoot = workspaceRoot;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessionId = sessionId!;
            }

            if (_initialized)
            {
                return;
            }

            _minimumDebugLogLevel = ParseMinimumDebugLevel();
            _debugFilter = ParseDebugFilter();
            _initialized = true;
            EnsureHooksInstalled();
            Perfetto.Initialize(_runId, _sessionId);
        }
    }

    public static void UpdateSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (SyncRoot)
        {
            _sessionId = sessionId!;
            Perfetto.UpdateSessionId(_sessionId);
        }
    }

    public static bool IsDebugMode()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return ShouldWriteDebugLogs();
        }
    }

    public static bool EnableDebugLogging()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            var wasActive = ShouldWriteDebugLogs() || IsAntUser();
            _runtimeDebugEnabled = true;
            return wasActive;
        }
    }

    public static void SetHasFormattedOutput(bool value)
    {
        lock (SyncRoot)
        {
            _hasFormattedOutput = value;
        }
    }

    public static string GetDebugLogPath()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return GetConfiguredDebugLogPath();
        }
    }

    public static string GetTelemetryEventsPath()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return GetTelemetryFilePath("telemetry", $"events.{_runId}.jsonl");
        }
    }

    public static string GetMetricsPath()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return GetTelemetryFilePath("telemetry", $"metrics.{_runId}.jsonl");
        }
    }

    public static string GetCrashPath()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return GetTelemetryFilePath("telemetry", $"crash.{_runId}.jsonl");
        }
    }

    public static string GetPerfettoTracePath()
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            return Perfetto.GetTracePath(_runId);
        }
    }

    public static void LogDebug(string message, DebugLogLevel level = DebugLogLevel.Debug)
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            if (!ShouldLogDebugMessage(message, level))
            {
                return;
            }

            var output = FormatDebugOutput(message, level, colorize: IsDebugToStdErr());
            if (IsDebugToStdErr())
            {
                Console.Error.Write(output);
                return;
            }

            AppendTextLine(GetConfiguredDebugLogPath(), output);
            TryUpdateLatestDebugSymlink();
        }
    }

    public static void LogEvent(string eventName, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            if (IsTestEnvironment())
            {
                return;
            }

            var record = new TelemetryEventRecord(
                eventName,
                DateTimeOffset.UtcNow,
                _sessionId,
                _runId,
                _eventSequence++,
                _workspaceRoot,
                metadata is null
                    ? null
                    : new Dictionary<string, object?>(metadata, StringComparer.Ordinal));
            AppendJsonLine(GetTelemetryFilePath("telemetry", $"events.{_runId}.jsonl"), record);
        }
    }

    public static void RecordMetric(
        string name,
        double value = 1,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            if (IsTestEnvironment())
            {
                return;
            }

            var record = new MetricRecord(
                name,
                value,
                DateTimeOffset.UtcNow,
                _sessionId,
                _runId,
                tags is null
                    ? null
                    : new Dictionary<string, object?>(tags, StringComparer.Ordinal));
            AppendJsonLine(GetTelemetryFilePath("telemetry", $"metrics.{_runId}.jsonl"), record);
        }
    }

    public static IClawSharpTelemetrySpan StartInteractionSpan(string userPrompt)
    {
        EnsureInitializedCore();
        var promptToLog = IsEnvTruthy(Environment.GetEnvironmentVariable("OTEL_LOG_USER_PROMPTS"))
            ? userPrompt
            : "<REDACTED>";
        return StartSpanInternal(
            "interaction",
            "claude_code.interaction",
            new Dictionary<string, object?>
            {
                ["user_prompt"] = promptToLog,
                ["user_prompt_length"] = userPrompt.Length
            },
            InteractionContext,
            parentContext: null,
            "interaction",
            "Interaction");
    }

    public static IClawSharpTelemetrySpan StartToolSpan(
        string toolName,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        EnsureInitializedCore();
        var mergedAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tool_name"] = toolName
        };
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                mergedAttributes[pair.Key] = pair.Value;
            }
        }

        return StartSpanInternal(
            "tool",
            "claude_code.tool",
            mergedAttributes,
            ToolContext,
            InteractionContext.Value,
            "tool",
            $"Tool: {toolName}");
    }

    public static IClawSharpTelemetrySpan StartSpan(
        string spanType,
        string name,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        EnsureInitializedCore();
        return StartSpanInternal(
            spanType,
            name,
            attributes,
            storage: null,
            parentContext: ToolContext.Value ?? InteractionContext.Value,
            spanType,
            name);
    }

    public static void CaptureException(
        Exception exception,
        string context,
        IReadOnlyDictionary<string, object?>? metadata = null,
        bool fatal = false)
    {
        lock (SyncRoot)
        {
            EnsureInitializedCore();
            var combinedMetadata = metadata is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(metadata, StringComparer.Ordinal);
            combinedMetadata["context"] = context;
            combinedMetadata["fatal"] = fatal;

            var record = new CrashRecord(
                DateTimeOffset.UtcNow,
                _sessionId,
                _runId,
                context,
                fatal,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.ToString(),
                combinedMetadata);

            AppendJsonLine(GetTelemetryFilePath("telemetry", $"crash.{_runId}.jsonl"), record);
            LogEvent(
                fatal ? "tengu_crash_captured" : "tengu_error_captured",
                new Dictionary<string, object?>
                {
                    ["context"] = context,
                    ["fatal"] = fatal,
                    ["exception_type"] = record.ExceptionType
                });
            RecordMetric(
                fatal ? "crash.count" : "error.count",
                1,
                new Dictionary<string, object?>
                {
                    ["context"] = context,
                    ["fatal"] = fatal
                });
            LogDebug(
                $"[{context}] {exception}",
                fatal ? DebugLogLevel.Error : DebugLogLevel.Warn);
        }
    }

    public static async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitializedCore();
        await Perfetto.FlushAsync(cancellationToken);
    }

    public static void ResetForTesting()
    {
        lock (SyncRoot)
        {
            _initialized = false;
            _hooksInstalled = false;
            _hasFormattedOutput = false;
            _runtimeDebugEnabled = false;
            _runId = Guid.NewGuid().ToString("N");
            _sessionId = _runId;
            _workspaceRoot = null;
            _eventSequence = 0;
            _minimumDebugLogLevel = DebugLogLevel.Debug;
            _debugFilter = null;
            InteractionContext.Value = null;
            ToolContext.Value = null;
            Perfetto.ResetForTesting();
        }
    }

    private static TelemetrySpan StartSpanInternal(
        string spanType,
        string name,
        IReadOnlyDictionary<string, object?>? attributes,
        AsyncLocal<TelemetrySpan?>? storage,
        TelemetrySpan? parentContext,
        string perfettoCategory,
        string perfettoName)
    {
        var mergedAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["span.type"] = spanType
        };
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                mergedAttributes[pair.Key] = pair.Value;
            }
        }

        var activityContext = parentContext?.Activity?.Context ?? default;
        var activity = ActivitySource.StartActivity(name, ActivityKind.Internal, activityContext);
        if (activity is not null)
        {
            foreach (var pair in mergedAttributes)
            {
                activity.SetTag(pair.Key, ToActivityTagValue(pair.Value));
            }
        }

        var perfettoSpanId = Perfetto.StartSpan(perfettoName, perfettoCategory, mergedAttributes);
        var span = new TelemetrySpan(name, spanType, activity, perfettoSpanId, storage, parentContext);
        if (storage is not null)
        {
            storage.Value = span;
        }

        return span;
    }

    private static void EnsureInitializedCore()
    {
        if (_initialized)
        {
            return;
        }

        Initialize(Directory.GetCurrentDirectory());
    }

    private static void EnsureHooksInstalled()
    {
        if (_hooksInstalled)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _hooksInstalled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            CaptureException(exception, "appdomain.unhandled_exception", fatal: args.IsTerminating);
            FlushAsync().GetAwaiter().GetResult();
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        CaptureException(args.Exception, "task_scheduler.unobserved_task_exception");
        args.SetObserved();
    }

    private static void OnProcessExit(object? sender, EventArgs args)
    {
        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Process-exit flush is best-effort only.
        }
    }

    private static string GetConfiguredDebugLogPath()
    {
        var debugFile = GetDebugFilePathFromArgs();
        if (!string.IsNullOrWhiteSpace(debugFile))
        {
            return debugFile!;
        }

        var directoryOverride = Environment.GetEnvironmentVariable("CLAUDE_CODE_DEBUG_LOGS_DIR");
        if (!string.IsNullOrWhiteSpace(directoryOverride))
        {
            return Path.Combine(directoryOverride, $"{_runId}.txt");
        }

        return GetTelemetryFilePath("debug", $"{_runId}.txt");
    }

    private static string GetTelemetryFilePath(string directoryName, string fileName)
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), directoryName, fileName);
    }

    private static void AppendTextLine(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        writer.Write(value);
    }

    private static void AppendJsonLine<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        writer.Write(JsonSerializer.Serialize(value, JsonOptions));
        writer.WriteLine();
    }

    private static bool ShouldLogDebugMessage(string message, DebugLogLevel level)
    {
        if (LevelOrder[level] < LevelOrder[_minimumDebugLogLevel])
        {
            return false;
        }

        if (IsTestEnvironment() && !IsDebugToStdErr())
        {
            return false;
        }

        if (!ShouldWriteDebugLogs())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_debugFilter))
        {
            return true;
        }

        return message.Contains(_debugFilter!, StringComparison.OrdinalIgnoreCase);
    }

    private static string PrepareDebugMessage(string message)
    {
        var value = _hasFormattedOutput && message.Contains(Environment.NewLine, StringComparison.Ordinal)
            ? JsonSerializer.Serialize(message)
            : message;
        return value.Trim();
    }

    private static string FormatDebugOutput(string message, DebugLogLevel level, bool colorize)
    {
        var timestamp = $"{DateTimeOffset.UtcNow:O}";
        var levelLabel = $"[{level.ToString().ToUpperInvariant()}]";
        var preparedMessage = PrepareDebugMessage(message);
        if (!colorize || !ShouldColorizeTerminalOutput())
        {
            return $"{timestamp} {levelLabel} {preparedMessage}{Environment.NewLine}";
        }

        const string reset = "\u001b[0m";
        const string dim = "\u001b[90m";
        var levelColor = level switch
        {
            DebugLogLevel.Verbose => "\u001b[90m",
            DebugLogLevel.Debug => "\u001b[36m",
            DebugLogLevel.Info => "\u001b[32m",
            DebugLogLevel.Warn => "\u001b[33m",
            DebugLogLevel.Error => "\u001b[31m",
            _ => reset
        };

        return $"{dim}{timestamp}{reset} {levelColor}{levelLabel}{reset} {preparedMessage}{Environment.NewLine}";
    }

    private static bool ShouldWriteDebugLogs()
    {
        return
            _runtimeDebugEnabled ||
            IsAntUser() ||
            IsEnvTruthy(Environment.GetEnvironmentVariable("DEBUG")) ||
            IsEnvTruthy(Environment.GetEnvironmentVariable("DEBUG_SDK")) ||
            Environment.GetCommandLineArgs().Any(
                static arg => arg is "--debug" or "-d" or "--debug-to-stderr" or "-d2e" || arg.StartsWith("--debug=", StringComparison.Ordinal)) ||
            !string.IsNullOrWhiteSpace(GetDebugFilePathFromArgs());
    }

    private static bool IsDebugToStdErr()
    {
        return Environment.GetCommandLineArgs().Any(static arg => arg is "--debug-to-stderr" or "-d2e");
    }

    private static bool ShouldColorizeTerminalOutput()
    {
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        if (!string.IsNullOrWhiteSpace(noColor))
        {
            return false;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        return !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDebugFilePathFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith("--debug-file=", StringComparison.Ordinal))
            {
                return arg["--debug-file=".Length..];
            }

            if (arg == "--debug-file" && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static DebugLogLevel ParseMinimumDebugLevel()
    {
        var raw = Environment.GetEnvironmentVariable("CLAUDE_CODE_DEBUG_LOG_LEVEL");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DebugLogLevel.Debug;
        }

        return Enum.TryParse<DebugLogLevel>(raw, ignoreCase: true, out var level)
            ? level
            : DebugLogLevel.Debug;
    }

    private static string? ParseDebugFilter()
    {
        return Environment.GetCommandLineArgs()
            .FirstOrDefault(static arg => arg.StartsWith("--debug=", StringComparison.Ordinal))
            ?["--debug=".Length..];
    }

    private static bool IsAntUser()
    {
        return string.Equals(Environment.GetEnvironmentVariable("USER_TYPE"), "ant", StringComparison.Ordinal);
    }

    private static bool IsTestEnvironment()
    {
        return string.Equals(Environment.GetEnvironmentVariable("NODE_ENV"), "test", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnvTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static void TryUpdateLatestDebugSymlink()
    {
        try
        {
            var currentPath = GetConfiguredDebugLogPath();
            var directory = Path.GetDirectoryName(currentPath)!;
            Directory.CreateDirectory(directory);
            var latestPath = Path.Combine(directory, "latest");
            if (File.Exists(latestPath) || Directory.Exists(latestPath))
            {
                File.Delete(latestPath);
            }

            File.CreateSymbolicLink(latestPath, currentPath);
        }
        catch
        {
            // Best-effort parity with the TS symlink update path.
        }
    }

    private static object? ToActivityTagValue(object? value)
    {
        return value switch
        {
            null => null,
            string stringValue => stringValue,
            bool boolValue => boolValue,
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            IEnumerable<string> strings => strings.ToArray(),
            _ => JsonSerializer.Serialize(value, JsonOptions)
        };
    }

    private sealed record TelemetryEventRecord(
        string EventName,
        DateTimeOffset Timestamp,
        string SessionId,
        string RunId,
        long Sequence,
        string? WorkspaceRoot,
        IReadOnlyDictionary<string, object?>? Metadata);

    private sealed record MetricRecord(
        string Name,
        double Value,
        DateTimeOffset Timestamp,
        string SessionId,
        string RunId,
        IReadOnlyDictionary<string, object?>? Tags);

    private sealed record CrashRecord(
        DateTimeOffset Timestamp,
        string SessionId,
        string RunId,
        string Context,
        bool Fatal,
        string ExceptionType,
        string Message,
        string StackTrace,
        IReadOnlyDictionary<string, object?> Metadata);

    private sealed class TelemetrySpan : IClawSharpTelemetrySpan
    {
        private readonly AsyncLocal<TelemetrySpan?>? _storage;
        private readonly TelemetrySpan? _parent;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public TelemetrySpan(
            string name,
            string spanType,
            Activity? activity,
            string? perfettoSpanId,
            AsyncLocal<TelemetrySpan?>? storage,
            TelemetrySpan? parent)
        {
            Name = name;
            SpanType = spanType;
            Activity = activity;
            PerfettoSpanId = perfettoSpanId;
            _storage = storage;
            _parent = parent;
        }

        public string Name { get; }

        public string SpanType { get; }

        public Activity? Activity { get; }

        public string? PerfettoSpanId { get; }

        public void SetAttribute(string key, object? value)
        {
            if (_disposed || Activity is null)
            {
                return;
            }

            Activity.SetTag(key, ToActivityTagValue(value));
        }

        public void AddEvent(string name, IReadOnlyDictionary<string, object?>? attributes = null)
        {
            if (_disposed || Activity is null)
            {
                return;
            }

            var tags = attributes is null
                ? default
                : new ActivityTagsCollection(
                    attributes.Select(
                        static pair => new KeyValuePair<string, object?>(pair.Key, ToActivityTagValue(pair.Value))));
            Activity.AddEvent(new ActivityEvent(name, default, tags));
        }

        public void RecordException(Exception exception)
        {
            if (_disposed || Activity is null)
            {
                return;
            }

            Activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            Activity.AddEvent(
                new ActivityEvent(
                    "exception",
                    default,
                    new ActivityTagsCollection
                    {
                        ["exception.type"] = exception.GetType().FullName,
                        ["exception.message"] = exception.Message,
                        ["exception.stacktrace"] = exception.ToString()
                    }));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SetAttribute("duration_ms", _stopwatch.Elapsed.TotalMilliseconds);
            Activity?.Dispose();
            Perfetto.EndSpan(
                PerfettoSpanId,
                new Dictionary<string, object?>
                {
                    ["duration_ms"] = _stopwatch.Elapsed.TotalMilliseconds
                });
            if (_storage is not null)
            {
                _storage.Value = _parent;
            }
        }
    }

    private sealed class PerfettoTracer
    {
        private readonly object _syncRoot = new();
        private readonly List<TraceEvent> _metadataEvents = [];
        private readonly List<TraceEvent> _events = [];
        private readonly Dictionary<string, PendingSpan> _pendingSpans = new(StringComparer.Ordinal);
        private bool _enabled;
        private string? _tracePath;
        private string _sessionId = string.Empty;
        private long _startTimestampMs;
        private int _spanCounter;
        private bool _flushed;

        public void Initialize(string runId, string sessionId)
        {
            lock (_syncRoot)
            {
                if (_enabled)
                {
                    return;
                }

                var envValue = Environment.GetEnvironmentVariable("CLAUDE_CODE_PERFETTO_TRACE");
                if (string.IsNullOrWhiteSpace(envValue) || string.Equals(envValue, "0", StringComparison.Ordinal))
                {
                    return;
                }

                _enabled = true;
                _sessionId = sessionId;
                _startTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _tracePath = IsEnvTruthy(envValue)
                    ? Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "traces", $"trace-{runId}.json")
                    : envValue;
                _metadataEvents.Add(
                    new TraceEvent(
                        "process_name",
                        "__metadata",
                        "M",
                        0,
                        1,
                        0,
                        args: new Dictionary<string, object?> { ["name"] = "main" }));
            }
        }

        public void UpdateSessionId(string sessionId)
        {
            lock (_syncRoot)
            {
                _sessionId = sessionId;
            }
        }

        public string GetTracePath(string runId)
        {
            lock (_syncRoot)
            {
                return _tracePath ?? Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "traces", $"trace-{runId}.json");
            }
        }

        public string? StartSpan(string name, string category, IReadOnlyDictionary<string, object?>? args)
        {
            lock (_syncRoot)
            {
                if (!_enabled)
                {
                    return null;
                }

                var spanId = $"span_{++_spanCounter}";
                var startUs = GetTimestampMicroseconds();
                var payload = args is null
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>(args, StringComparer.Ordinal);
                _pendingSpans[spanId] = new PendingSpan(name, category, payload);
                _events.Add(new TraceEvent(name, category, "B", startUs, 1, 1, args: payload));
                return spanId;
            }
        }

        public void EndSpan(string? spanId, IReadOnlyDictionary<string, object?>? args)
        {
            if (string.IsNullOrWhiteSpace(spanId))
            {
                return;
            }

            lock (_syncRoot)
            {
                if (!_enabled || !_pendingSpans.Remove(spanId!, out var pending))
                {
                    return;
                }

                var payload = new Dictionary<string, object?>(pending.Args, StringComparer.Ordinal);
                if (args is not null)
                {
                    foreach (var pair in args)
                    {
                        payload[pair.Key] = pair.Value;
                    }
                }

                _events.Add(new TraceEvent(pending.Name, pending.Category, "E", GetTimestampMicroseconds(), 1, 1, args: payload));
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? tracePath;
            string document;
            lock (_syncRoot)
            {
                if (!_enabled || _flushed || string.IsNullOrWhiteSpace(_tracePath))
                {
                    return;
                }

                foreach (var pending in _pendingSpans.Values.ToArray())
                {
                    _events.Add(
                        new TraceEvent(
                            pending.Name,
                            pending.Category,
                            "E",
                            GetTimestampMicroseconds(),
                            1,
                            1,
                            args: new Dictionary<string, object?>(pending.Args, StringComparer.Ordinal)
                            {
                                ["incomplete"] = true
                            }));
                }

                _pendingSpans.Clear();
                document = JsonSerializer.Serialize(
                    new
                    {
                        traceEvents = _metadataEvents.Concat(_events).ToArray(),
                        metadata = new Dictionary<string, object?>
                        {
                            ["session_id"] = _sessionId,
                            ["trace_start_time"] = DateTimeOffset.FromUnixTimeMilliseconds(_startTimestampMs).ToString("O"),
                            ["total_event_count"] = _metadataEvents.Count + _events.Count
                        }
                    },
                    JsonOptions);
                tracePath = _tracePath;
                _flushed = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(tracePath!)!);
            await File.WriteAllTextAsync(tracePath!, document, cancellationToken);
        }

        public void ResetForTesting()
        {
            lock (_syncRoot)
            {
                _metadataEvents.Clear();
                _events.Clear();
                _pendingSpans.Clear();
                _enabled = false;
                _tracePath = null;
                _sessionId = string.Empty;
                _startTimestampMs = 0;
                _spanCounter = 0;
                _flushed = false;
            }
        }

        private long GetTimestampMicroseconds()
        {
            return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTimestampMs) * 1000;
        }

        private sealed record PendingSpan(
            string Name,
            string Category,
            IReadOnlyDictionary<string, object?> Args);

        private sealed record TraceEvent(
            string name,
            string cat,
            string ph,
            long ts,
            int pid,
            int tid,
            long? dur = null,
            IReadOnlyDictionary<string, object?>? args = null);
    }
}
