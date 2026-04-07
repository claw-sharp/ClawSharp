using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class DefaultMemoryPathResolverTests
{
    [Fact]
    public void GetMemoryDir_Uses_Validated_Environment_Override()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-memory-paths", Guid.NewGuid().ToString("N"));
        var overridePath = Path.Combine(Path.GetTempPath(), "clawsharp-memory-override", Guid.NewGuid().ToString("N"));
        var originalOverride = Environment.GetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE");

        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE", overridePath);

        try
        {
            var resolver = new DefaultMemoryPathResolver(new ClawSharpSettings(), Path.Combine(Path.GetTempPath(), "memory-base"));

            var result = resolver.GetMemoryDir(workspaceRoot);

            Assert.Equal(Path.GetFullPath(overridePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE", originalOverride);
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GetMemoryDir_Rejects_Dangerous_Environment_Override_And_Falls_Back()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-memory-paths", Guid.NewGuid().ToString("N"));
        var originalOverride = Environment.GetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE");
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE", Path.GetPathRoot(workspaceRoot));

        try
        {
            var memoryBase = Path.Combine(Path.GetTempPath(), "memory-base", Guid.NewGuid().ToString("N"));
            var resolver = new DefaultMemoryPathResolver(new ClawSharpSettings(), memoryBase);

            var result = resolver.GetMemoryDir(workspaceRoot);

            Assert.Equal(Path.Combine(memoryBase, "projects", SessionStoragePaths.SanitizePath(workspaceRoot), "memory"), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_COWORK_MEMORY_PATH_OVERRIDE", originalOverride);
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GetMemoryDir_Expands_Tilde_From_Settings_Path()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-memory-paths", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settings = new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    AutoMemoryDirectory = "~/.clawsharp-memory"
                }
            };
            var resolver = new DefaultMemoryPathResolver(settings, Path.Combine(Path.GetTempPath(), "memory-base"));

            var result = resolver.GetMemoryDir(workspaceRoot);

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".clawsharp-memory").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.Equal(expected, result);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
