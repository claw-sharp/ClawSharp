using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class SessionFactoryTests
{
    [Fact]
    public async Task ResumeAsync_Rehydrates_Existing_Session_With_Recorded_Messages()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-factory-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var factory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var fresh = factory.Create();
            fresh.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));
            fresh.Add(
                ChatMessageFactory.CreateToolResult(
                    "tooluse-task-output",
                    "TaskOutput",
                    "<retrieval_status>success</retrieval_status>",
                    new JsonObject
                    {
                        ["retrieval_status"] = "success",
                        ["task"] = new JsonObject
                        {
                            ["task_id"] = "task-123",
                            ["task_type"] = "local_bash",
                            ["status"] = "completed",
                            ["description"] = "bash task",
                            ["output"] = "done\n"
                        }
                    }));
            await transcriptStore.RecordTranscriptAsync(fresh, fresh.Messages);

            var resumed = await factory.ResumeAsync(fresh.Id);

            Assert.NotNull(resumed);
            Assert.Equal(fresh.Id, resumed!.Id);
            Assert.Equal(2, resumed.Messages.Count);
            Assert.All(resumed.Messages, message => Assert.True(resumed.HasRecorded(message.Id)));
            var toolResult = resumed.Messages[1].ContentBlocks[0];
            Assert.Equal(MessageContentKind.ToolResult, toolResult.Kind);
            Assert.Equal("tooluse-task-output", toolResult.Metadata?["toolUseId"]);
            Assert.True(toolResult.Metadata?.ContainsKey("structuredOutput"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResumeAsync_Restores_Custom_Title_And_File_History_State()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-file-history-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var factory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var fresh = factory.Create();
            fresh.SetCustomTitle("Named Session");
            fresh.EnsureFileHistorySnapshot("assistant-message-1");
            fresh.TrackFileHistoryBackup(
                "assistant-message-1",
                Path.Combine(workspaceRoot, "src", "app.cs"),
                new FileHistoryBackup("abc123@v1", 1, DateTimeOffset.UtcNow));
            fresh.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));

            await transcriptStore.RecordTranscriptAsync(fresh, fresh.Messages);
            var resumed = await factory.ResumeAsync(fresh.Id);

            Assert.NotNull(resumed);
            Assert.Equal("Named Session", resumed!.CustomTitle);
            Assert.Single(resumed.FileHistoryState.Snapshots);
            Assert.Single(resumed.FileHistoryState.TrackedFiles);
            Assert.Contains(Path.Combine("src", "app.cs"), resumed.FileHistoryState.TrackedFiles);
            var snapshot = resumed.FileHistoryState.Snapshots[0];
            Assert.Equal("assistant-message-1", snapshot.MessageId);
            Assert.Equal("abc123@v1", snapshot.TrackedFileBackups[Path.Combine("src", "app.cs")].BackupFileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ContinueMostRecentAsync_Loads_Latest_Project_Transcript()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-continue-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var factory = new DefaultSessionFactory(workspaceRoot, transcriptStore);

            var older = factory.Create();
            older.Add(ChatMessageFactory.CreateText(MessageRole.User, "older"));
            await transcriptStore.RecordTranscriptAsync(older, older.Messages);
            File.SetLastWriteTimeUtc(older.TranscriptPath, DateTime.UtcNow.AddMinutes(-10));

            var newer = factory.Create();
            newer.Add(ChatMessageFactory.CreateText(MessageRole.User, "newer"));
            await transcriptStore.RecordTranscriptAsync(newer, newer.Messages);
            File.SetLastWriteTimeUtc(newer.TranscriptPath, DateTime.UtcNow);

            var continued = await factory.ContinueMostRecentAsync();

            Assert.NotNull(continued);
            Assert.Equal(newer.Id, continued!.Id);
            Assert.Single(continued.Messages);
            Assert.Equal("newer", continued.Messages[0].Content);
            Assert.True(continued.HasRecorded(continued.Messages[0].Id));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
