using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class CoreDomainModelCoverageTests
{
    [Fact]
    public void ConversationSession_Tracks_CustomTitle_FileHistory_And_Attribution_State()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-core-session-tests");

        try
        {
            var session = new ConversationSession("session-123", workspaceRoot);

            Assert.Equal("session-123", session.Id);
            Assert.Equal(Path.GetFullPath(workspaceRoot), session.ProjectDirectory);
            Assert.Equal(SessionStoragePaths.GetTranscriptPath(workspaceRoot, "session-123"), session.TranscriptPath);
            Assert.Equal("cli", session.AttributionState.Surface);

            session.SetCustomTitle("  Incident Review  ");
            Assert.Equal("Incident Review", session.CustomTitle);
            Assert.True(session.HasUnrecordedCustomTitle());

            session.MarkCustomTitleRecorded();
            Assert.False(session.HasUnrecordedCustomTitle());

            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));
            session.AddRecorded(ChatMessageFactory.CreateText(MessageRole.Assistant, "world"));
            Assert.Equal(2, session.Messages.Count);
            Assert.True(session.HasRecorded(session.Messages[1].Id));

            session.EnsureFileHistorySnapshot("assistant-1");
            session.TrackFileHistoryBackup(
                "assistant-1",
                Path.Combine("src", "app.cs"),
                new FileHistoryBackup("backup-1", 1, new DateTimeOffset(2026, 4, 5, 18, 0, 0, TimeSpan.Zero)));

            var pendingSnapshots = session.DrainPendingFileHistorySnapshots();
            Assert.Equal(2, pendingSnapshots.Count);
            Assert.False(pendingSnapshots[0].IsSnapshotUpdate);
            Assert.True(pendingSnapshots[1].IsSnapshotUpdate);
            Assert.Equal("assistant-1", pendingSnapshots[1].Snapshot.MessageId);
            Assert.Equal("backup-1", pendingSnapshots[1].Snapshot.TrackedFileBackups[Path.Combine("src", "app.cs")].BackupFileName);

            var restoredHistory = new FileHistoryState(
                [
                    new FileHistorySnapshot(
                        "assistant-2",
                        new Dictionary<string, FileHistoryBackup>(StringComparer.Ordinal)
                        {
                            [Path.Combine("docs", "plan.md")] = new("backup-2", 2, new DateTimeOffset(2026, 4, 5, 18, 1, 0, TimeSpan.Zero))
                        },
                        new DateTimeOffset(2026, 4, 5, 18, 1, 0, TimeSpan.Zero))
                ],
                new HashSet<string>(StringComparer.Ordinal)
                {
                    Path.Combine("docs", "plan.md")
                },
                SnapshotSequence: 7);
            var restoredAttribution = AttributionState.CreateEmpty("desktop") with
            {
                PromptCount = 3,
                PermissionPromptCount = 1
            };

            session.RestoreCustomTitle("  Resumed Session  ");
            session.RestoreFileHistoryState(restoredHistory);
            session.RestoreAttributionState(restoredAttribution);

            Assert.Equal("Resumed Session", session.CustomTitle);
            Assert.Single(session.FileHistoryState.Snapshots);
            Assert.Contains(Path.Combine("docs", "plan.md"), session.FileHistoryState.TrackedFiles);
            Assert.Equal(7, session.FileHistoryState.SnapshotSequence);
            Assert.Equal(3, session.AttributionState.PromptCount);
            Assert.Equal("desktop", session.AttributionState.Surface);
            Assert.Empty(session.DrainPendingFileHistorySnapshots());
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void ChatMessageFactory_Creates_Ts_Shaped_Content_Blocks_And_Metadata()
    {
        var user = ChatMessageFactory.CreateUserMessage(string.Empty, isMeta: true);
        var toolUse = ChatMessageFactory.CreateToolUse(
            [
                ("tooluse-read", "Read", """{"file_path":"README.md"}""")
            ]);
        var toolResult = ChatMessageFactory.CreateToolResult(
            "tooluse-read",
            "Read",
            "README contents",
            new JsonObject
            {
                ["type"] = "text",
                ["file"] = new JsonObject
                {
                    ["filePath"] = "README.md"
                }
            });
        var progress = ChatMessageFactory.CreateProgress(
            "progress-read",
            "tooluse-read",
            new JsonObject
            {
                ["type"] = "mcp_progress",
                ["status"] = "started"
            });
        var hookSuccess = ChatMessageFactory.CreateHookSuccessAttachmentMessage(
            "all good",
            "PostToolUse",
            "tooluse-read",
            HookEvent.PostToolUse,
            stdout: "stdout",
            stderr: string.Empty,
            exitCode: 0,
            command: "echo ok",
            durationMs: 12);

        Assert.Equal(MessageRole.User, user.Role);
        Assert.Equal(ChatMessageFactory.NoContentMessage, user.Content);
        Assert.Equal("True", user.ContentBlocks[0].Metadata?["isMeta"]);

        Assert.Equal(MessageRole.Assistant, toolUse.Role);
        Assert.Equal(MessageContentKind.ToolUse, Assert.Single(toolUse.ContentBlocks).Kind);
        Assert.Equal("Read", toolUse.ContentBlocks[0].Name);
        Assert.Equal("tooluse-read", toolUse.ContentBlocks[0].Metadata?["toolUseId"]);

        Assert.Equal(MessageRole.User, toolResult.Role);
        Assert.Equal(MessageContentKind.ToolResult, Assert.Single(toolResult.ContentBlocks).Kind);
        Assert.Equal("tooluse-read", toolResult.ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Contains("README.md", toolResult.ContentBlocks[0].Metadata?["structuredOutput"], StringComparison.Ordinal);

        Assert.Equal(MessageRole.System, progress.Role);
        Assert.Equal(MessageContentKind.Progress, Assert.Single(progress.ContentBlocks).Kind);
        Assert.Equal("progress-read", progress.ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Equal("tooluse-read", progress.ContentBlocks[0].Metadata?["parentToolUseId"]);

        Assert.Equal(MessageRole.System, hookSuccess.Role);
        Assert.Equal(MessageContentKind.Attachment, Assert.Single(hookSuccess.ContentBlocks).Kind);
        Assert.Equal("hook_success", hookSuccess.ContentBlocks[0].Metadata?["attachmentType"]);
        Assert.Equal("PostToolUse", hookSuccess.ContentBlocks[0].Metadata?["hookName"]);
        Assert.Equal("echo ok", hookSuccess.ContentBlocks[0].Metadata?["command"]);
    }

    [Fact]
    public void Core_Record_Models_Retain_Ts_Shaped_Metadata_And_Defaults()
    {
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string"
                }
            }
        };
        var annotations = new McpToolAnnotations(ReadOnlyHint: true, DestructiveHint: false, Title: "idempotent");
        var tool = new McpToolDefinition(
            "inspect_file",
            "Inspect a file",
            inputSchema,
            annotations,
            new JsonObject
            {
                ["anthropic/searchHint"] = "inspect file"
            });
        var prompt = new McpPromptDefinition(
            "review",
            "Review a file",
            [
                new McpPromptArgumentDefinition("path", true, "Path to review")
            ]);
        var resource = new McpResourceDefinition("file:///repo/README.md", "README", "text/markdown", "Workspace readme");
        var manifest = new PluginManifest(
            "reviewer",
            "Code review plugin",
            "1.2.3",
            Commands: ["review"],
            Agents: ["reviewer"],
            Skills: ["skills/reviewer"],
            OutputStyles: ["compact"],
            HookFiles: ["hooks/hooks.json"],
            McpServers: [],
            Settings: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["agent"] = "reviewer"
            },
            UserConfig: new Dictionary<string, PluginOptionDefinition>(StringComparer.Ordinal)
            {
                ["workspaceRoot"] = new(PluginOptionType.Directory, "Workspace root", "Directory to review", Required: true)
            });
        var installation = new DiscoveredPluginInstallation(
            "reviewer@anthropic-tools",
            PluginInstallationScope.User,
            @"C:\plugins\reviewer",
            ProjectPath: null,
            Version: "1.2.3",
            InstalledAt: "2026-04-05T18:00:00Z",
            LastUpdated: "2026-04-05T18:05:00Z",
            GitCommitSha: "abc123");
        var skill = new DiscoveredSkill("reviewer", @"C:\plugins\reviewer\skills\reviewer\SKILL.md", @"C:\plugins\reviewer\skills\reviewer", "plugin");
        var bridge = new BridgeSessionInfo("env-1", "session-1", true);
        var remote = new RemoteSessionInfo("https://worker.example.test/session-1", "connected");
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "opus",
                PermissionMode = PermissionMode.Plan
            },
            Terminal = new TerminalSettings
            {
                ShowTimestamps = true,
                UseColor = false
            }
        };

        Assert.Equal("inspect_file", tool.Name);
        Assert.Equal("object", tool.InputSchema?["type"]?.GetValue<string>());
        Assert.True(tool.Annotations?.ReadOnlyHint);
        Assert.Equal("inspect file", tool.Meta?["anthropic/searchHint"]?.GetValue<string>());

        Assert.Equal("review", prompt.Name);
        Assert.Equal("path", Assert.Single(prompt.Arguments!).Name);
        Assert.Equal("file:///repo/README.md", resource.Uri);
        Assert.Equal("Workspace readme", resource.Description);

        Assert.Equal("reviewer", manifest.Name);
        Assert.Equal("review", Assert.Single(manifest.Commands));
        Assert.Equal("reviewer", Assert.Single(manifest.Agents));
        Assert.Equal("skills/reviewer", Assert.Single(manifest.Skills));
        Assert.Equal("compact", Assert.Single(manifest.OutputStyles));
        Assert.Equal("reviewer", manifest.Settings?["agent"]);
        Assert.True(manifest.UserConfig?["workspaceRoot"].Required);

        Assert.Equal(PluginInstallationScope.User, installation.Scope);
        Assert.Equal("abc123", installation.GitCommitSha);
        Assert.Equal("reviewer", skill.Name);
        Assert.Equal("plugin", skill.Source);
        Assert.True(bridge.IsConnected);
        Assert.Equal("connected", remote.ConnectionStatus);
        Assert.Equal("opus", settings.Runtime.Model);
        Assert.Equal(PermissionMode.Plan, settings.Runtime.PermissionMode);
        Assert.True(settings.Terminal.ShowTimestamps);
        Assert.False(settings.Terminal.UseColor);
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
