// TS origin: ./tools/AgentTool/forkSubagent.ts, ./constants/xml.ts
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class ForkSubagentFoundationTests
{
    [Fact]
    public void ForkAgentDefinition_Matches_Ts_Synthetic_Fork_Agent()
    {
        var definition = ForkSubagentFoundation.ForkAgentDefinition;

        Assert.Equal("fork", definition.AgentType);
        Assert.Equal("built-in", definition.Source);
        Assert.Equal("built-in", definition.BaseDirectory);
        Assert.Equal(["*"], definition.Tools);
        Assert.Equal("inherit", definition.Model);
        Assert.Equal(PermissionMode.Bubble, definition.PermissionMode);
        Assert.Equal(200, definition.MaxTurns);
    }

    [Fact]
    public void IsInForkChild_Returns_True_When_User_Message_Contains_Fork_Boilerplate_Tag()
    {
        ChatMessage[] messages =
        [
            ChatMessageFactory.CreateText(MessageRole.Assistant, "working"),
            ChatMessageFactory.CreateText(MessageRole.User, "<fork-boilerplate>directive</fork-boilerplate>")
        ];

        Assert.True(ForkSubagentFoundation.IsInForkChild(messages));
    }

    [Fact]
    public void BuildForkedMessages_Clones_Assistant_Message_And_Adds_Placeholder_Tool_Results()
    {
        var assistantMessage = new ChatMessage(
            "assistant-1",
            MessageRole.Assistant,
            [
                new MessageContentBlock(MessageContentKind.Text, "prelude"),
                new MessageContentBlock(
                    MessageContentKind.ToolUse,
                    """{"path":"src/app.ts"}""",
                    "Read",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["toolUseId"] = "toolu_1" }),
                new MessageContentBlock(
                    MessageContentKind.ToolUse,
                    """{"command":"dotnet test"}""",
                    "Bash",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["toolUseId"] = "toolu_2" })
            ],
            DateTimeOffset.UtcNow);

        var result = ForkSubagentFoundation.BuildForkedMessages("inspect the task runtime", assistantMessage);

        Assert.Equal(2, result.Count);

        var clonedAssistant = result[0];
        Assert.Equal(MessageRole.Assistant, clonedAssistant.Role);
        Assert.NotEqual(assistantMessage.Id, clonedAssistant.Id);
        Assert.Equal(assistantMessage.ContentBlocks.Count, clonedAssistant.ContentBlocks.Count);

        var userMessage = result[1];
        Assert.Equal(MessageRole.User, userMessage.Role);
        Assert.Equal(MessageContentKind.ToolResult, userMessage.ContentBlocks[0].Kind);
        Assert.Equal("toolu_1", userMessage.ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Equal(MessageContentKind.ToolResult, userMessage.ContentBlocks[1].Kind);
        Assert.Equal("toolu_2", userMessage.ContentBlocks[1].Metadata?["toolUseId"]);
        Assert.Equal(MessageContentKind.Text, userMessage.ContentBlocks[2].Kind);
        Assert.Contains("STOP. READ THIS FIRST.", userMessage.ContentBlocks[2].Value);
        Assert.Contains("Your directive: inspect the task runtime", userMessage.ContentBlocks[2].Value);
    }

    [Fact]
    public void BuildForkedMessages_Falls_Back_To_Single_User_Message_When_No_Tool_Uses_Are_Present()
    {
        var assistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, "no tool uses here");

        var result = ForkSubagentFoundation.BuildForkedMessages("inspect the task runtime", assistantMessage);

        var message = Assert.Single(result);
        Assert.Equal(MessageRole.User, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Contains("Your directive: inspect the task runtime", block.Value);
    }

    [Fact]
    public void BuildWorktreeNotice_Matches_Ts_Path_Translation_Guidance()
    {
        var notice = ForkSubagentFoundation.BuildWorktreeNotice("C:\\repo", "C:\\repo-worktree");

        Assert.Contains("parent agent working in C:\\repo", notice);
        Assert.Contains("isolated git worktree at C:\\repo-worktree", notice);
        Assert.Contains("translate them to your worktree root", notice);
    }
}
