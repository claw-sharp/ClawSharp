using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class ToolContractsAndValidationCoverageTests
{
    [Fact]
    public void ToolExecutionContext_Reports_Progress_Messages_And_AppState()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-tool-contracts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var observedProgress = new List<ToolProgressUpdate>();
            var observedMessages = new List<ChatMessage>();
            var session = new ConversationSession("session-1", workspaceRoot);
            var appStateStore = new NullClawSharpAppStateStore(workspaceRoot);
            var context = new ToolExecutionContext(
                Arguments: """{"file_path":"README.md"}""",
                WorkspaceRoot: workspaceRoot,
                Session: session,
                AppStateStore: appStateStore,
                Tasks: new TaskRegistry(workspaceRoot),
                TaskAppState: new TaskRegistry(workspaceRoot),
                Settings: new ClawSharpSettings(),
                ReadFileState: FileStateCache.CreateWithSizeLimit(16),
                ToolPermissionContext: ToolPermissionContexts.CreateEmpty(PermissionMode.Plan),
                AgentDefinitions: [],
                FileUpdateNotifier: new NullFileUpdateNotifier(),
                PermissionPrompter: new NullPermissionPrompter(),
                OnProgress: observedProgress.Add,
                OnMessage: observedMessages.Add,
                QuerySource: "repl",
                CurrentSystemPrompt: ["system prompt"]);

            context.ReportProgress(
                "tooluse-read",
                new JsonObject
                {
                    ["type"] = "progress",
                    ["status"] = "started"
                });
            context.ReportMessage(ChatMessageFactory.CreateText(MessageRole.System, "working"));

            var progress = Assert.Single(observedProgress);
            Assert.Equal("tooluse-read", progress.ToolUseId);
            Assert.Equal("progress", progress.Data["type"]?.GetValue<string>());
            Assert.Equal("started", progress.Data["status"]?.GetValue<string>());

            var message = Assert.Single(observedMessages);
            Assert.Equal(MessageRole.System, message.Role);
            Assert.Equal("working", message.Content);

            Assert.Equal(workspaceRoot, context.WorkspaceRoot);
            Assert.Equal("repl", context.QuerySource);
            Assert.Equal(PermissionMode.Default, context.AppState.ToolPermissionContext.Mode);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ToolValidationResult_Static_Factories_Preserve_Validity_And_Error_Message()
    {
        var valid = ToolValidationResult.Valid();
        var invalid = ToolValidationResult.Invalid("file_path is required");

        Assert.True(valid.IsValid);
        Assert.Null(valid.ErrorMessage);

        Assert.False(invalid.IsValid);
        Assert.Equal("file_path is required", invalid.ErrorMessage);
    }

    [Fact]
    public void ToolDescriptor_And_ExecutionResult_Retain_Ts_Shaped_Schema_Metadata()
    {
        var descriptor = new ToolDescriptor(
            "Read",
            "Read a file",
            IsLongRunningCapable: false,
            Parameters:
            [
                new ToolParameter("file_path", "Path to read", true)
            ],
            Aliases:
            [
                "cat"
            ],
            SearchHint: "read files from disk",
            ShouldDefer: false,
            AlwaysLoad: true,
            InputSchema: new JsonObject
            {
                ["type"] = "object"
            },
            OutputSchema: new JsonObject
            {
                ["type"] = "object"
            },
            Strict: true);
        var result = new ToolExecutionResult(
            true,
            "README contents",
            new JsonObject
            {
                ["type"] = "text",
                ["file"] = new JsonObject
                {
                    ["filePath"] = "README.md"
                }
            });

        Assert.Equal("Read", descriptor.Name);
        Assert.Equal("read files from disk", descriptor.SearchHint);
        Assert.True(descriptor.AlwaysLoad);
        Assert.True(descriptor.Strict);
        Assert.Equal("file_path", Assert.Single(descriptor.Parameters!).Name);
        Assert.Equal("cat", Assert.Single(descriptor.Aliases!));
        Assert.Equal("object", descriptor.InputSchema?["type"]?.GetValue<string>());
        Assert.Equal("object", descriptor.OutputSchema?["type"]?.GetValue<string>());

        Assert.True(result.Success);
        Assert.Equal("README contents", result.Output);
        Assert.Equal("text", result.StructuredOutput?["type"]?.GetValue<string>());
        Assert.Equal("README.md", result.StructuredOutput?["file"]?["filePath"]?.GetValue<string>());
    }
}
