using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class TranscriptStoreTests
{
    [Fact]
    public async Task RecordTranscript_Writes_Only_New_Messages_To_Jsonl()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();

            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));
            session.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "world"));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            Assert.Equal(
                SessionStoragePaths.GetTranscriptPath(workspaceRoot, session.Id),
                session.TranscriptPath);
            Assert.True(File.Exists(session.TranscriptPath));

            var lines = await File.ReadAllLinesAsync(session.TranscriptPath);
            Assert.Equal(2, lines.Length);

            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal("user", first.RootElement.GetProperty("type").GetString());
            Assert.Equal("user", first.RootElement.GetProperty("message").GetProperty("role").GetString());
            Assert.Equal(
                "hello",
                first.RootElement
                    .GetProperty("message")
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString());
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
    public async Task RecordTranscript_Persists_Structured_Tool_Result_Metadata()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-structured-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
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

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var line = Assert.Single(await File.ReadAllLinesAsync(session.TranscriptPath));
            using var json = JsonDocument.Parse(line);
            var content = json.RootElement
                .GetProperty("message")
                .GetProperty("content")[0];

            Assert.Equal("tool_result", content.GetProperty("type").GetString());
            Assert.Equal("tooluse-task-output", content.GetProperty("tool_use_id").GetString());
            Assert.Equal("success", content.GetProperty("structured_output").GetProperty("retrieval_status").GetString());
            Assert.Equal("local_bash", content.GetProperty("structured_output").GetProperty("task").GetProperty("task_type").GetString());
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
    public async Task ReadTranscript_RoundTrips_Tool_Result_And_Progress_Messages()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-roundtrip-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(ChatMessageFactory.CreateToolUse([("tooluse-task-output", "TaskOutput", """{"task_id":"task-123"}""")]));
            session.Add(
                ChatMessageFactory.CreateProgress(
                    "progress-1",
                    "tooluse-task-output",
                    new JsonObject
                    {
                        ["type"] = "waiting_for_task",
                        ["taskDescription"] = "tracked task",
                        ["taskType"] = "local_bash"
                    }));
            session.Add(
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

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            Assert.Equal(3, loaded.Messages.Count);
            Assert.Equal(MessageRole.Assistant, loaded.Messages[0].Role);
            Assert.Equal(MessageContentKind.ToolUse, loaded.Messages[0].ContentBlocks[0].Kind);
            Assert.Equal("tooluse-task-output", loaded.Messages[0].ContentBlocks[0].Metadata?["toolUseId"]);

            Assert.Equal(MessageRole.System, loaded.Messages[1].Role);
            Assert.Equal(MessageContentKind.Progress, loaded.Messages[1].ContentBlocks[0].Kind);
            var progressData = Assert.IsType<JsonObject>(JsonNode.Parse(loaded.Messages[1].ContentBlocks[0].Value));
            Assert.Equal("waiting_for_task", progressData["type"]?.GetValue<string>());
            Assert.Equal("tooluse-task-output", loaded.Messages[1].ContentBlocks[0].Metadata?["parentToolUseId"]);

            Assert.Equal(MessageRole.User, loaded.Messages[2].Role);
            Assert.Equal(MessageContentKind.ToolResult, loaded.Messages[2].ContentBlocks[0].Kind);
            Assert.Equal("tooluse-task-output", loaded.Messages[2].ContentBlocks[0].Metadata?["toolUseId"]);
            var structuredOutput = Assert.IsType<JsonObject>(JsonNode.Parse(loaded.Messages[2].ContentBlocks[0].Metadata!["structuredOutput"]));
            Assert.Equal("success", structuredOutput["retrieval_status"]?.GetValue<string>());
            Assert.Equal("local_bash", structuredOutput["task"]?["task_type"]?.GetValue<string>());
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
    public async Task ReadTranscript_RoundTrips_Metadata_Backed_System_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-system-message-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateStopHookSummaryMessage(
                    hookCount: 1,
                    hookInfos: [new StopHookInfo("git status", "workspace check")],
                    hookErrors: ["blocked"],
                    preventedContinuation: true,
                    stopReason: "hook_blocked",
                    hasOutput: false,
                    level: "warning",
                    toolUseId: "tool-1",
                    hookLabel: "PostToolUse",
                    totalDurationMs: 25));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Text, block.Kind);
            Assert.NotNull(block.Metadata);
            Assert.Equal("stop_hook_summary", block.Metadata!["subtype"]);
            Assert.Equal("1", block.Metadata["hookCount"]);
            Assert.Equal("warning", block.Metadata["level"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
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
    public async Task ReadTranscript_RoundTrips_Metadata_Backed_Assistant_Api_Error_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-assistant-api-error-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateAssistantApiErrorMessage(
                    content: "Rate limited",
                    apiError: "rate_limit_error",
                    error: "rate_limit",
                    errorDetails: "retry later"));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.Assistant, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Text, block.Kind);
            Assert.Equal("Rate limited", block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("True", block.Metadata!["isApiErrorMessage"]);
            Assert.Equal("rate_limit_error", block.Metadata["apiError"]);
            Assert.Equal("rate_limit", block.Metadata["error"]);
            Assert.Equal("retry later", block.Metadata["errorDetails"]);
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
    public async Task ReadTranscript_RoundTrips_Metadata_Backed_System_Api_Error_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-system-api-error-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateSystemApiErrorMessage(
                    new JsonObject
                    {
                        ["name"] = "APIConnectionError",
                        ["message"] = "connection reset"
                    },
                    retryInMs: 2500,
                    retryAttempt: 2,
                    maxRetries: 5,
                    cause: new JsonObject
                    {
                        ["name"] = "IOException",
                        ["message"] = "socket closed"
                    }));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Text, block.Kind);
            Assert.Equal("Model API Error: connection reset Retrying in 2s (Attempt 2 of 5)...", block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("api_error", block.Metadata!["subtype"]);
            Assert.Equal("error", block.Metadata["level"]);
            Assert.Equal("2500", block.Metadata["retryInMs"]);
            Assert.Equal("2", block.Metadata["retryAttempt"]);
            Assert.Equal("5", block.Metadata["maxRetries"]);
            Assert.Contains("APIConnectionError", block.Metadata["error"]);
            Assert.Contains("IOException", block.Metadata["cause"]);
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
    public async Task ReadTranscript_RoundTrips_Metadata_Backed_Compact_Boundary_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-compact-boundary-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateCompactBoundaryMessage(
                    trigger: "auto",
                    preTokens: 12345,
                    lastPreCompactMessageUuid: "msg-1",
                    userContext: "ctx",
                    messagesSummarized: 7));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal("Conversation compacted", block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("compact_boundary", block.Metadata!["subtype"]);
            Assert.Equal("msg-1", block.Metadata["logicalParentUuid"]);
            Assert.Contains("\"Trigger\":\"auto\"", block.Metadata["compactMetadata"]);
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
    public async Task ReadTranscript_RoundTrips_Metadata_Backed_Informational_System_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-informational-system-message-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateSystemMessage(
                    content: "Hook blocked continuation",
                    level: "warning",
                    toolUseId: "tool-1",
                    preventContinuation: true));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal("Hook blocked continuation", block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("informational", block.Metadata!["subtype"]);
            Assert.Equal("warning", block.Metadata["level"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("True", block.Metadata["preventContinuation"]);
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
    public async Task ReadTranscript_RoundTrips_MaxTurnsReached_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-max-turns-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
                    maxTurns: 3,
                    turnCount: 4));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("max_turns_reached", block.Metadata!["attachmentType"]);
            Assert.Equal("3", block.Metadata["maxTurns"]);
            Assert.Equal("4", block.Metadata["turnCount"]);
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
    public async Task ReadTranscript_RoundTrips_HookStoppedContinuation_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-stopped-continuation-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookStoppedContinuationAttachmentMessage(
                    message: "Stop hook prevented continuation",
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_stopped_continuation", block.Metadata!["attachmentType"]);
            Assert.Equal("Stop hook prevented continuation", block.Metadata["message"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
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
    public async Task ReadTranscript_RoundTrips_StructuredOutput_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-structured-output-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateStructuredOutputAttachmentMessage(
                    JsonNode.Parse("""{"status":"ok","payload":{"id":42}}""")!));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("structured_output", block.Metadata!["attachmentType"]);
            Assert.Equal("""{"status":"ok","payload":{"id":42}}""", block.Metadata["data"]);
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
    public async Task ReadTranscript_RoundTrips_OutputTokenUsage_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-output-token-usage-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(
                    turn: 123,
                    session: 456,
                    budget: 789));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("output_token_usage", block.Metadata!["attachmentType"]);
            Assert.Equal("123", block.Metadata["turn"]);
            Assert.Equal("456", block.Metadata["session"]);
            Assert.Equal("789", block.Metadata["budget"]);
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
    public async Task ReadTranscript_RoundTrips_HookSystemMessage_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-system-message-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookSystemMessageAttachmentMessage(
                    content: "Hook says hello",
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_system_message", block.Metadata!["attachmentType"]);
            Assert.Equal("Hook says hello", block.Metadata["content"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
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
    public async Task ReadTranscript_RoundTrips_HookPermissionDecision_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-permission-decision-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookPermissionDecisionAttachmentMessage(
                    decision: "allow",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.PermissionRequest));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_permission_decision", block.Metadata!["attachmentType"]);
            Assert.Equal("allow", block.Metadata["decision"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("PermissionRequest", block.Metadata["hookEvent"]);
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
    public async Task ReadTranscript_RoundTrips_HookAdditionalContext_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-additional-context-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookAdditionalContextAttachmentMessage(
                    ["line 1", "line 2"],
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_additional_context", block.Metadata!["attachmentType"]);
            Assert.Equal("""["line 1","line 2"]""", block.Metadata["content"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
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
    public async Task ReadTranscript_RoundTrips_HookBlockingError_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-blocking-error-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookBlockingErrorAttachmentMessage(
                    new HookBlockingError("blocked by policy", "echo nope"),
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_blocking_error", block.Metadata!["attachmentType"]);
            Assert.Equal("""{"BlockingError":"blocked by policy","Command":"echo nope"}""", block.Metadata["blockingError"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
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
    public async Task ReadTranscript_RoundTrips_HookSuccess_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-success-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookSuccessAttachmentMessage(
                    content: "success text",
                    hookName: "SessionStart",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.SessionStart,
                    stdout: "out",
                    stderr: "err",
                    exitCode: 0,
                    command: "echo hi",
                    durationMs: 123));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_success", block.Metadata!["attachmentType"]);
            Assert.Equal("success text", block.Metadata["content"]);
            Assert.Equal("SessionStart", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("SessionStart", block.Metadata["hookEvent"]);
            Assert.Equal("out", block.Metadata["stdout"]);
            Assert.Equal("err", block.Metadata["stderr"]);
            Assert.Equal("0", block.Metadata["exitCode"]);
            Assert.Equal("echo hi", block.Metadata["command"]);
            Assert.Equal("123", block.Metadata["durationMs"]);
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
    public async Task ReadTranscript_RoundTrips_HookCancelled_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-cancelled-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookCancelledAttachmentMessage(
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop,
                    command: "echo hi",
                    durationMs: 123));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_cancelled", block.Metadata!["attachmentType"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
            Assert.Equal("echo hi", block.Metadata["command"]);
            Assert.Equal("123", block.Metadata["durationMs"]);
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
    public async Task ReadTranscript_RoundTrips_HookErrorDuringExecution_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-error-during-execution-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookErrorDuringExecutionAttachmentMessage(
                    content: "hook crashed",
                    hookName: "Stop",
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop,
                    command: "echo nope",
                    durationMs: 123));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_error_during_execution", block.Metadata!["attachmentType"]);
            Assert.Equal("hook crashed", block.Metadata["content"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
            Assert.Equal("echo nope", block.Metadata["command"]);
            Assert.Equal("123", block.Metadata["durationMs"]);
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
    public async Task ReadTranscript_RoundTrips_HookNonBlockingError_Attachment_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-hook-non-blocking-error-attachment-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.Add(
                ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
                    hookName: "Stop",
                    stderr: "stderr text",
                    stdout: "stdout text",
                    exitCode: 2,
                    toolUseId: "tool-1",
                    hookEvent: HookEvent.Stop,
                    command: "echo nope",
                    durationMs: 123));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            var message = Assert.Single(loaded.Messages);
            Assert.Equal(MessageRole.System, message.Role);
            var block = Assert.Single(message.ContentBlocks);
            Assert.Equal(MessageContentKind.Attachment, block.Kind);
            Assert.Equal(string.Empty, block.Value);
            Assert.NotNull(block.Metadata);
            Assert.Equal("hook_non_blocking_error", block.Metadata!["attachmentType"]);
            Assert.Equal("Stop", block.Metadata["hookName"]);
            Assert.Equal("stderr text", block.Metadata["stderr"]);
            Assert.Equal("stdout text", block.Metadata["stdout"]);
            Assert.Equal("2", block.Metadata["exitCode"]);
            Assert.Equal("tool-1", block.Metadata["toolUseID"]);
            Assert.Equal("Stop", block.Metadata["hookEvent"]);
            Assert.Equal("echo nope", block.Metadata["command"]);
            Assert.Equal("123", block.Metadata["durationMs"]);
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
    public async Task ReadTranscript_Restores_Custom_Title_And_File_History_Metadata()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-transcript-file-history-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            session.SetCustomTitle("Incident Review");
            session.EnsureFileHistorySnapshot("assistant-message-1");
            session.TrackFileHistoryBackup(
                "assistant-message-1",
                "src/app.cs",
                new FileHistoryBackup("abc123@v1", 1, DateTimeOffset.UtcNow));
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));

            await transcriptStore.RecordTranscriptAsync(session, session.Messages);
            var loaded = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath);

            Assert.Equal("Incident Review", loaded.CustomTitle);
            Assert.Single(loaded.Messages);
            Assert.Single(loaded.FileHistoryState.Snapshots);
            Assert.Single(loaded.FileHistoryState.TrackedFiles);
            Assert.Contains("src/app.cs", loaded.FileHistoryState.TrackedFiles);
            var snapshot = loaded.FileHistoryState.Snapshots[0];
            Assert.Equal("assistant-message-1", snapshot.MessageId);
            Assert.Equal("abc123@v1", snapshot.TrackedFileBackups["src/app.cs"].BackupFileName);
            Assert.Equal(1, loaded.FileHistoryState.SnapshotSequence);
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
