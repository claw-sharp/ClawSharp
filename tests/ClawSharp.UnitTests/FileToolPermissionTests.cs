// TS origin: ./utils/permissions/filesystem.ts, ./tools/FileReadTool/FileReadTool.ts, ./tools/FileEditTool/FileEditTool.ts, ./tools/FileWriteTool/FileWriteTool.ts
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class FileToolPermissionTests
{
    [Fact]
    public async Task Read_Allows_Absolute_Path_In_Additional_Working_Directory()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-workspace", Guid.NewGuid().ToString("N"));
        var extraRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-extra", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(extraRoot);

        try
        {
            var filePath = Path.Combine(extraRoot, "outside.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(
                    additionalWorkingDirectories: new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal)
                    {
                        [extraRoot] = new AdditionalWorkingDirectory(extraRoot, PermissionRuleSource.Session)
                    }));

            var result = await registry.ExecuteAsync(
                "Read",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Equal("hello", result.Output);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(extraRoot))
            {
                Directory.Delete(extraRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Read_Outside_Allowed_Working_Directories_Requests_Permission()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-blocked-workspace", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-blocked-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var filePath = Path.Combine(outsideRoot, "outside.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var registry = new ToolRegistry(workspaceRoot, new TaskRegistry());
            var result = await registry.ExecuteAsync(
                "Read",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal(
                $"Claude requested permissions to read from {filePath}, but you haven't granted it yet.",
                result.Output);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_AcceptEdits_Allows_Absolute_Path_In_Additional_Working_Directory()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-workspace", Guid.NewGuid().ToString("N"));
        var extraRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-extra", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(extraRoot);

        try
        {
            var filePath = Path.Combine(extraRoot, "write.txt");
            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(
                    mode: PermissionMode.AcceptEdits,
                    additionalWorkingDirectories: new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal)
                    {
                        [extraRoot] = new AdditionalWorkingDirectory(extraRoot, PermissionRuleSource.Session)
                    }));

            var result = await registry.ExecuteAsync(
                "Write",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}"",""content"":""allowed""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Equal("allowed", await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(extraRoot))
            {
                Directory.Delete(extraRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_Deny_Rule_Takes_Precedence_Over_AcceptEdits_Mode()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-deny-workspace", Guid.NewGuid().ToString("N"));
        var extraRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-deny-extra", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(extraRoot);

        try
        {
            var filePath = Path.Combine(extraRoot, "blocked.txt");
            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(
                    mode: PermissionMode.AcceptEdits,
                    additionalWorkingDirectories: new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal)
                    {
                        [extraRoot] = new AdditionalWorkingDirectory(extraRoot, PermissionRuleSource.Session)
                    },
                    alwaysDenyRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                    {
                        [PermissionRuleSource.Session] = [filePath]
                    }));

            var result = await registry.ExecuteAsync(
                "Write",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}"",""content"":""blocked""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal($"Permission to edit {filePath} has been denied.", result.Output);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(extraRoot))
            {
                Directory.Delete(extraRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_Allow_Rule_Allows_Outside_Workspace_In_Default_Mode()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-allow-workspace", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-allow-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var filePath = Path.Combine(outsideRoot, "allowed.txt");
            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(
                    alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                    {
                        [PermissionRuleSource.Session] = [filePath]
                    }));

            var result = await registry.ExecuteAsync(
                "Write",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}"",""content"":""allowed""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Equal("allowed", await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Read_Symlink_Inside_Workspace_To_Outside_File_Requests_Permission()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-symlink-workspace", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-read-symlink-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var outsideFilePath = Path.Combine(outsideRoot, "outside.txt");
            var linkPath = Path.Combine(workspaceRoot, "linked.txt");
            await File.WriteAllTextAsync(outsideFilePath, "secret");
            CreateFileSymlink(linkPath, outsideFilePath);

            var registry = new ToolRegistry(workspaceRoot, new TaskRegistry());
            var result = await registry.ExecuteAsync(
                "Read",
                "linked.txt",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal(
                $"Claude requested permissions to read from {linkPath}, but you haven't granted it yet.",
                result.Output);
        }
        finally
        {
            DeleteFileIfExists(Path.Combine(workspaceRoot, "linked.txt"));
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_Symlinked_Subdirectory_Inside_Workspace_To_Outside_Path_Requests_Permission()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-symlink-workspace", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-symlink-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var linkDirectory = Path.Combine(workspaceRoot, "linked-dir");
            CreateDirectorySymlink(linkDirectory, outsideRoot);

            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(mode: PermissionMode.AcceptEdits));

            var result = await registry.ExecuteAsync(
                "Write",
                """{"file_path":"linked-dir/new.txt","content":"blocked"}""",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal(
                $"Claude requested permissions to write to {Path.Combine(workspaceRoot, "linked-dir", "new.txt")}, but you haven't granted it yet.",
                result.Output);
            Assert.False(File.Exists(Path.Combine(outsideRoot, "new.txt")));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.Combine(workspaceRoot, "linked-dir"));
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_Allow_Rule_Matches_Resolved_Symlink_Target()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-symlink-allow-workspace", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-symlink-allow-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var linkDirectory = Path.Combine(workspaceRoot, "linked-dir");
            var targetFilePath = Path.Combine(outsideRoot, "allowed.txt");
            CreateDirectorySymlink(linkDirectory, outsideRoot);

            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(
                    alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                    {
                        [PermissionRuleSource.Session] = [targetFilePath]
                    }));

            var result = await registry.ExecuteAsync(
                "Write",
                """{"file_path":"linked-dir/allowed.txt","content":"allowed"}""",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Equal("allowed", await File.ReadAllTextAsync(targetFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.Combine(workspaceRoot, "linked-dir"));
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Write_AcceptEdits_Still_Requests_Permission_For_Dangerous_File()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-write-dangerous-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var filePath = Path.Combine(workspaceRoot, ".gitconfig");
            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(),
                toolPermissionContext: CreatePermissionContext(mode: PermissionMode.AcceptEdits));

            var result = await registry.ExecuteAsync(
                "Write",
                $@"{{""file_path"":""{filePath.Replace("\\", "\\\\")}"",""content"":""blocked""}}",
                new DefaultSessionFactory(workspaceRoot).Create(),
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal($"Claude requested permissions to edit {filePath} which is a sensitive file.", result.Output);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static ToolPermissionContext CreatePermissionContext(
        PermissionMode mode = PermissionMode.Default,
        IReadOnlyDictionary<string, AdditionalWorkingDirectory>? additionalWorkingDirectories = null,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysAllowRules = null,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysDenyRules = null,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysAskRules = null)
    {
        return new ToolPermissionContext(
            mode,
            additionalWorkingDirectories ?? new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            alwaysAllowRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            alwaysDenyRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            alwaysAskRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: false);
    }

    private static void CreateFileSymlink(string linkPath, string targetPath)
    {
        File.CreateSymbolicLink(linkPath, targetPath);
    }

    private static void CreateDirectorySymlink(string linkPath, string targetPath)
    {
        Directory.CreateSymbolicLink(linkPath, targetPath);
    }

    private static void DeleteFileIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath);
        }
    }
}
