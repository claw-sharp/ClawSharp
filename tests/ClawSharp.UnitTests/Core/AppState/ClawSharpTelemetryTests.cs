using ClawSharp.Core;
using ClawSharp.Infrastructure;
using System.Reflection;

namespace ClawSharp.UnitTests;

[Collection("TelemetrySerial")]
public sealed class ClawSharpTelemetryTests
{
    [Fact]
    public async Task LogEvent_RecordMetric_AndCaptureException_WriteTelemetryArtifacts()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-telemetry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);
        ClawSharpTelemetry.ResetForTesting();

        try
        {
            ClawSharpTelemetry.Initialize(configDir, "session-telemetry");
            ClawSharpTelemetry.LogEvent(
                "unit_test_event",
                new Dictionary<string, object?>
                {
                    ["value"] = 7
                });
            ClawSharpTelemetry.RecordMetric("unit_test_metric", 42);
            ClawSharpTelemetry.CaptureException(new InvalidOperationException("boom"), "unit.test");
            await ClawSharpTelemetry.FlushAsync();

            Assert.Contains("unit_test_event", ReadAllTextShared(ClawSharpTelemetry.GetTelemetryEventsPath()), StringComparison.Ordinal);
            Assert.Contains("unit_test_metric", ReadAllTextShared(ClawSharpTelemetry.GetMetricsPath()), StringComparison.Ordinal);
            Assert.Contains("boom", ReadAllTextShared(ClawSharpTelemetry.GetCrashPath()), StringComparison.Ordinal);
        }
        finally
        {
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StartSpans_WritesPerfettoTrace_WhenEnabled()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var originalPerfetto = Environment.GetEnvironmentVariable("CLAUDE_CODE_PERFETTO_TRACE");
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-perfetto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_PERFETTO_TRACE", "1");
        ClawSharpTelemetry.ResetForTesting();

        try
        {
            ClawSharpTelemetry.Initialize(configDir, "session-perfetto");
            using (ClawSharpTelemetry.StartInteractionSpan("hello"))
            {
                using (ClawSharpTelemetry.StartToolSpan("Read"))
                {
                }
            }

            await ClawSharpTelemetry.FlushAsync();

            var trace = ReadAllTextShared(ClawSharpTelemetry.GetPerfettoTracePath());
            Assert.Contains("Interaction", trace, StringComparison.Ordinal);
            Assert.Contains("Tool: Read", trace, StringComparison.Ordinal);
        }
        finally
        {
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_PERFETTO_TRACE", originalPerfetto);
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StartupProfiler_LogsSampledStartupPerf_ForAntSessions()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        var originalProfileStartup = Environment.GetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP");
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-startup-telemetry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);

        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);
        Environment.SetEnvironmentVariable("USER_TYPE", "ant");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", null);
        ClawSharpTelemetry.ResetForTesting();
        StartupProfiler.ResetForTesting();

        try
        {
            ClawSharpTelemetry.Initialize(configDir, "session-startup");
            StartupProfiler.Checkpoint("cli_entry");
            StartupProfiler.Checkpoint("main_after_run");
            StartupProfiler.Report("session-startup");
            await ClawSharpTelemetry.FlushAsync();

            Assert.Contains("tengu_startup_perf", ReadAllTextShared(ClawSharpTelemetry.GetTelemetryEventsPath()), StringComparison.Ordinal);
        }
        finally
        {
            StartupProfiler.ResetForTesting();
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", originalProfileStartup);
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public void FormatDebugOutput_Colorizes_Level_For_Terminal_Output()
    {
        var originalNoColor = Environment.GetEnvironmentVariable("NO_COLOR");
        var originalTerm = Environment.GetEnvironmentVariable("TERM");
        Environment.SetEnvironmentVariable("NO_COLOR", null);
        Environment.SetEnvironmentVariable("TERM", "xterm-256color");

        try
        {
            var formatter = typeof(ClawSharpTelemetry).GetMethod("FormatDebugOutput", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(formatter);

            var colorized = Assert.IsType<string>(formatter!.Invoke(null, ["message", DebugLogLevel.Warn, true]));
            var plain = Assert.IsType<string>(formatter.Invoke(null, ["message", DebugLogLevel.Warn, false]));

            Assert.Contains("\u001b[33m[WARN]\u001b[0m", colorized, StringComparison.Ordinal);
            Assert.Contains("message", colorized, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b[", plain, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", originalNoColor);
            Environment.SetEnvironmentVariable("TERM", originalTerm);
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
