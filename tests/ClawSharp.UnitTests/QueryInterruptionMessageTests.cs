// TS parity status: focused C# unit coverage for the synthetic interruption-message helper; broader query abort handling still depends on the unported model-backed loop.
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public class QueryInterruptionMessageTests
{
    [Fact]
    public void CreateUserInterruptionMessage_Uses_Default_Interrupt_Text()
    {
        var message = ChatMessageFactory.CreateUserInterruptionMessage();

        Assert.Equal(MessageRole.User, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal(ChatMessageFactory.InterruptMessage, block.Value);
    }

    [Fact]
    public void CreateUserInterruptionMessage_Uses_Tool_Use_Interrupt_Text()
    {
        var message = ChatMessageFactory.CreateUserInterruptionMessage(toolUse: true);

        Assert.Equal(MessageRole.User, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal(ChatMessageFactory.InterruptMessageForToolUse, block.Value);
    }
}
