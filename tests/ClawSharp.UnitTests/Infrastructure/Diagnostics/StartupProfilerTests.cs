using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("TelemetrySerial")]
public sealed class StartupProfilerTests
{
    [Fact]
    public void Report_Writes_Detailed_Profile_When_Enabled()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var originalProfileStartup = Environment.GetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP");
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-startup-profiler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", "1");
        StartupProfiler.ResetForTesting();

        try
        {
            StartupProfiler.Checkpoint("cli_entry");
            StartupProfiler.Checkpoint("main_after_run");

            StartupProfiler.Report("session-123");

            var reportPath = ClaudeConfigPaths.GetStartupPerfLogPath("session-123");
            Assert.True(File.Exists(reportPath));

            var report = File.ReadAllText(reportPath);
            Assert.Contains("STARTUP PROFILING REPORT", report, StringComparison.Ordinal);
            Assert.Contains("cli_entry", report, StringComparison.Ordinal);
            Assert.Contains("main_after_run", report, StringComparison.Ordinal);
            Assert.Contains("Total startup time:", report, StringComparison.Ordinal);
        }
        finally
        {
            StartupProfiler.ResetForTesting();
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", originalProfileStartup);

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Report_Does_Not_Write_File_When_Profiling_Is_Disabled()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var originalProfileStartup = Environment.GetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP");
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-startup-profiler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", null);
        StartupProfiler.ResetForTesting();

        try
        {
            StartupProfiler.Checkpoint("cli_entry");
            StartupProfiler.Report("session-456");

            var reportPath = ClaudeConfigPaths.GetStartupPerfLogPath("session-456");
            Assert.False(File.Exists(reportPath));
        }
        finally
        {
            StartupProfiler.ResetForTesting();
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_PROFILE_STARTUP", originalProfileStartup);

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }
}
