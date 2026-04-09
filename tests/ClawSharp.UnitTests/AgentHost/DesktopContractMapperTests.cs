using ClawSharp.AgentHost.Mapping;
using ClawSharp.Core;

public sealed class DesktopContractMapperTests
{
    [Fact]
    public void MapThreadDetail_ExcludesToolResultOnlyMessagesFromDesktopTranscript()
    {
        var session = new ConversationSession(
            Guid.NewGuid().ToString("N"),
            Path.Combine(Path.GetTempPath(), "clawsharp-desktop-contract-mapper-tests"));

        var userMessage = ChatMessageFactory.CreateUserMessage("write a snake game");
        var toolResultMessage = ChatMessageFactory.CreateToolResult(
            "tool-use-1",
            "Glob",
            "Glob requires a non-empty pattern.");
        var assistantMessage = ChatMessageFactory.CreateText(
            MessageRole.Assistant,
            "I'll check the workspace, then I'll create the game.");

        session.Add(userMessage);
        session.Add(toolResultMessage);
        session.Add(assistantMessage);

        var detail = DesktopContractMapper.MapThreadDetail("project-1", session, DateTimeOffset.UtcNow);

        Assert.Collection(
            detail.Messages,
            message =>
            {
                Assert.Equal(userMessage.Id, message.Id);
                Assert.Equal("user", message.Role);
                Assert.Equal("write a snake game", message.Content);
            },
            message =>
            {
                Assert.Equal(assistantMessage.Id, message.Id);
                Assert.Equal("assistant", message.Role);
                Assert.Equal("I'll check the workspace, then I'll create the game.", message.Content);
            });
    }

    [Fact]
    public void ShouldSurfaceInDesktopThread_ReturnsFalseForToolResultOnlyUserMessages()
    {
        var toolResultMessage = ChatMessageFactory.CreateToolResult(
            "tool-use-1",
            "Read",
            "Invalid pages parameter: \"\".");

        Assert.False(DesktopContractMapper.ShouldSurfaceInDesktopThread(toolResultMessage));
    }
}
