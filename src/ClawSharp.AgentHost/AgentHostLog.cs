using ClawSharp.Core;

namespace ClawSharp.AgentHost;

internal static class AgentHostLog
{
    public static void Initialize(string workspaceRoot)
    {
        ClawSharpTelemetry.Initialize(workspaceRoot);
    }

    public static void Debug(string category, string message)
    {
        Write(DebugLogLevel.Debug, category, message);
    }

    public static void Info(string category, string message)
    {
        Write(DebugLogLevel.Info, category, message);
    }

    public static void Warn(string category, string message)
    {
        Write(DebugLogLevel.Warn, category, message);
    }

    public static void Error(string category, string message)
    {
        Write(DebugLogLevel.Error, category, message);
    }

    private static void Write(DebugLogLevel level, string category, string message)
    {
        ClawSharpTelemetry.LogDebug($"[AgentHost:{category}] {message}", level);
    }
}
