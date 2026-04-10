using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class FileToolHistoryTests
{
    [Fact]
    public async Task Write_Creates_Versioned_File_History_Backups_And_Transcript_Snapshot_Entries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-file-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configDir = Path.Combine(tempDir, ".clawsharp");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        try
        {
            var filePath = Path.Combine(tempDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "alpha");

            var eventSink = new InMemoryEventSink();
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(),
                fileUpdateNotifier: new EventSinkFileUpdateNotifier(eventSink));
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();
            var transcriptStore = new JsonlTranscriptStore();

            await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-1", "beta");
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-2", "gamma");
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var backupDirectory = SessionStoragePaths.GetFileHistorySessionDir(session.Id);
            var backupFiles = Directory.GetFiles(backupDirectory)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(2, backupFiles.Length);
            Assert.Contains(backupFiles, name => name!.EndsWith("@v1", StringComparison.Ordinal));
            Assert.Contains(backupFiles, name => name!.EndsWith("@v2", StringComparison.Ordinal));

            var transcriptLines = await File.ReadAllLinesAsync(session.TranscriptPath);
            var snapshotEntries = transcriptLines
                .Where(line => line.Contains("\"type\":\"file-history-snapshot\"", StringComparison.Ordinal))
                .Select(line => Assert.IsType<JsonObject>(JsonNode.Parse(line)))
                .ToArray();

            Assert.Equal(4, snapshotEntries.Length);
            Assert.Contains(snapshotEntries, entry => entry["isSnapshotUpdate"]?.GetValue<bool>() == false);
            Assert.Contains(snapshotEntries, entry => entry["isSnapshotUpdate"]?.GetValue<bool>() == true);
            Assert.Contains(
                snapshotEntries,
                entry => entry["snapshot"]?["trackedFileBackups"]?["sample.txt"]?["version"]?.GetValue<int>() == 1);
            Assert.Contains(
                snapshotEntries,
                entry => entry["snapshot"]?["trackedFileBackups"]?["sample.txt"]?["version"]?.GetValue<int>() == 2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Edit_Emits_File_Update_Notifications()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-file-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "alpha beta alpha");
            var timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

            var eventSink = new InMemoryEventSink();
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(),
                fileUpdateNotifier: new EventSinkFileUpdateNotifier(eventSink));
            var session = new DefaultSessionFactory(tempDir).Create();
            session.Add(ChatMessageFactory.CreateToolUse([("tooluse-edit", "Edit", """{"file_path":"sample.txt","old_string":"alpha","new_string":"omega","replace_all":true}""")]));
            registry.ReadFileState.Set(filePath, new FileState("alpha beta alpha", timestamp, null, null));

            var result = await registry.ExecuteAsync(
                "Edit",
                """{"file_path":"sample.txt","old_string":"alpha","new_string":"omega","replace_all":true}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success);
            var notifications = eventSink.Events
                .Where(appEvent => appEvent.Type == AppEventType.NotificationRaised)
                .ToArray();

            Assert.Contains(
                notifications,
                appEvent => appEvent.Metadata?["notificationType"] == "file-diagnostics-cleared" &&
                            appEvent.Metadata["filePath"] == filePath);
            Assert.Contains(
                notifications,
                appEvent => appEvent.Metadata?["notificationType"] == "file-updated" &&
                            appEvent.Metadata["filePath"] == filePath &&
                            appEvent.Metadata["oldContent"] == "alpha beta alpha" &&
                            appEvent.Metadata["newContent"] == "omega beta omega");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileHistory_Can_Rewind_To_Previous_Snapshot_And_Report_Diff_Stats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-file-history-rewind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configDir = Path.Combine(tempDir, ".clawsharp");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        try
        {
            var filePath = Path.Combine(tempDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "line1\nline2\n");

            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var firstMessageId = await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-1", "line1\nbeta\n");
            var secondMessageId = await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-2", "line1\ngamma\nextra\n");

            Assert.True(FileHistoryService.CanRestore(session, settings, secondMessageId));
            Assert.True(await FileHistoryService.HasAnyChangesAsync(session, settings, secondMessageId));

            var diffStats = await FileHistoryService.GetDiffStatsAsync(session, settings, secondMessageId);
            Assert.NotNull(diffStats);
            Assert.Contains(filePath, diffStats!.FilesChanged);
            Assert.True(diffStats.Insertions > 0);
            Assert.True(diffStats.Deletions > 0);

            var changedFiles = await FileHistoryService.RewindAsync(session, settings, secondMessageId);

            Assert.Contains(filePath, changedFiles);
            Assert.Equal("line1\nbeta\n", await File.ReadAllTextAsync(filePath));
            Assert.False(await FileHistoryService.HasAnyChangesAsync(session, settings, secondMessageId));
            Assert.NotEqual(firstMessageId, secondMessageId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileHistory_Rewind_Deletes_File_When_Target_Snapshot_Predates_Creation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-file-history-delete-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configDir = Path.Combine(tempDir, ".clawsharp");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        try
        {
            var filePath = Path.Combine(tempDir, "created.txt");
            var registry = new ToolRegistry(tempDir, new TaskRegistry());
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var creationSnapshotMessageId = await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-create-1", "first\n");
            await ExecuteWriteAsync(registry, session, settings, filePath, "tooluse-create-2", "second\n");

            Assert.True(File.Exists(filePath));
            Assert.True(await FileHistoryService.HasAnyChangesAsync(session, settings, creationSnapshotMessageId));

            var changedFiles = await FileHistoryService.RewindAsync(session, settings, creationSnapshotMessageId);

            Assert.Contains(filePath, changedFiles);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<string> ExecuteWriteAsync(
        ToolRegistry registry,
        ConversationSession session,
        ClawSharpSettings settings,
        string filePath,
        string toolUseId,
        string newContent)
    {
        var relativePath = Path.GetRelativePath(session.ProjectDirectory, filePath);
        var arguments = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["file_path"] = relativePath.Replace("\\", "/"),
                ["content"] = newContent
            });
        if (File.Exists(filePath))
        {
            var existingContent = await File.ReadAllTextAsync(filePath);
            var timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            registry.ReadFileState.Set(filePath, new FileState(existingContent, timestamp, null, null));
        }

        session.Add(ChatMessageFactory.CreateToolUse([(toolUseId, "Write", arguments)]));
        var messageId = session.Messages[^1].Id;

        var result = await registry.ExecuteAsync(
            "Write",
            arguments,
            session,
            settings);
        Assert.True(result.Success, result.Output);
        session.Add(ChatMessageFactory.CreateToolResult(toolUseId, "Write", result.Output, result.StructuredOutput));
        return messageId;
    }
}
