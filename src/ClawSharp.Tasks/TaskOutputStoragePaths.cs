using System.Runtime.InteropServices;
using ClawSharp.Core;

namespace ClawSharp.Tasks;

public static class TaskOutputStoragePaths
{
    public static string GetClaudeTempDir()
    {
        return Path.Combine(Path.GetTempPath(), GetClaudeTempDirName());
    }

    public static string GetProjectTempDir(string workspaceRoot)
    {
        return Path.Combine(
            GetClaudeTempDir(),
            SessionStoragePaths.SanitizePath(Path.GetFullPath(workspaceRoot)));
    }

    public static string GetTaskOutputDir(string workspaceRoot, string sessionId)
    {
        return Path.Combine(GetProjectTempDir(workspaceRoot), sessionId, "tasks");
    }

    public static string GetTaskOutputPath(string workspaceRoot, string sessionId, string taskId)
    {
        return Path.Combine(GetTaskOutputDir(workspaceRoot, sessionId), $"{taskId}.output");
    }

    public static string GetAgentTranscriptDir(string workspaceRoot, string sessionId)
    {
        return Path.Combine(
            SessionStoragePaths.GetProjectDir(Path.GetFullPath(workspaceRoot)),
            sessionId,
            "subagents");
    }

    public static string GetAgentTranscriptPath(string workspaceRoot, string sessionId, string agentId, string? subdir = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(subdir)
            ? GetAgentTranscriptDir(workspaceRoot, sessionId)
            : Path.Combine(GetAgentTranscriptDir(workspaceRoot, sessionId), subdir);
        return Path.Combine(baseDirectory, $"agent-{agentId}.jsonl");
    }

    public static string GetAgentMetadataPath(string workspaceRoot, string sessionId, string agentId, string? subdir = null)
    {
        return Path.ChangeExtension(GetAgentTranscriptPath(workspaceRoot, sessionId, agentId, subdir), ".meta.json");
    }

    private static string GetClaudeTempDirName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "claude";
        }

        return $"claude-{GetCurrentUid()}";
    }

    private static uint GetCurrentUid()
    {
        try
        {
            return getuid();
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint getuid();
}
