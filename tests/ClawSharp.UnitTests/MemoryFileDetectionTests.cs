// TS origin: ./utils/memoryFileDetection.ts
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class MemoryFileDetectionTests
{
    [Fact]
    public void DetectSessionFileType_Returns_SessionMemory_For_Markdown_Under_Config_Directory()
    {
        var configHome = CombinePath("home", "user", ".claude");
        var filePath = CombinePath("home", "user", ".claude", "session-memory", "session-1.md");

        var result = MemoryFileDetection.DetectSessionFileType(filePath, configHome);

        Assert.Equal("session_memory", result);
    }

    [Fact]
    public void IsMemoryDirectory_Does_Not_Treat_Project_Memory_As_Tracked_When_AutoMemory_Is_Disabled()
    {
        var memoryBase = CombinePath("home", "user", ".claude", "projects", "repo");
        var directoryPath = CombinePath("home", "user", ".claude", "projects", "repo", "memory");

        var result = MemoryFileDetection.IsMemoryDirectory(directoryPath, memoryBase);

        Assert.False(result);
    }

    [Fact]
    public void IsShellCommandTargetingMemory_Does_Not_False_Positive_When_AutoMemory_Is_Disabled()
    {
        var configHome = CombinePath("home", "user", ".claude");
        var memoryBase = CombinePath("home", "user", ".claude", "projects", "repo");
        var command = $"grep needle {CombinePath("home", "user", ".claude", "notes.md")}";

        var result = MemoryFileDetection.IsShellCommandTargetingMemory(
            command,
            memoryBase,
            autoMemoryDirectory: null,
            configHomeDir: configHome);

        Assert.False(result);
    }

    [Fact]
    public void IsShellCommandTargetingMemory_Returns_True_For_SessionMemory_File()
    {
        var configHome = CombinePath("home", "user", ".claude");
        var memoryBase = CombinePath("home", "user", ".claude", "projects", "repo");
        var command = $"cat {CombinePath("home", "user", ".claude", "session-memory", "session-2.md")}";

        var result = MemoryFileDetection.IsShellCommandTargetingMemory(
            command,
            memoryBase,
            autoMemoryDirectory: null,
            configHomeDir: configHome);

        Assert.True(result);
    }

    [Fact]
    public void IsAutoManagedMemoryPattern_Matches_Session_Patterns_Only_By_Default()
    {
        Assert.True(MemoryFileDetection.IsAutoManagedMemoryPattern("session-memory/*.md"));
        Assert.False(MemoryFileDetection.IsAutoManagedMemoryPattern("projects/repo/memory/*.md"));
    }

    [Fact]
    public void IsAutoManagedMemoryPattern_Matches_AgentMemory_When_AutoMemory_Is_Enabled()
    {
        var result = MemoryFileDetection.IsAutoManagedMemoryPattern(
            "agent-memory/**/*.md",
            autoMemoryEnabled: true);

        Assert.True(result);
    }

    private static string CombinePath(params string[] segments)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(["C:\\", .. segments]);
        }

        return Path.Combine(["/", .. segments]);
    }
}
