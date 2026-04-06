// TS parity status: focused C# unit coverage for metadata-backed system-message helpers used as the closest viable alternative to the TypeScript discriminated system-message union.
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public class QuerySystemMessageContractTests
{
    [Fact]
    public void CreateStopHookSummaryMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateStopHookSummaryMessage(
            hookCount: 2,
            hookInfos:
            [
                new StopHookInfo("git diff", "check workspace"),
                new StopHookInfo("dotnet test")
            ],
            hookErrors: ["hook failed"],
            preventedContinuation: true,
            stopReason: "blocked",
            hasOutput: true,
            level: "warning",
            toolUseId: "tool-1",
            hookLabel: "PostToolUse",
            totalDurationMs: 1234);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("stop_hook_summary", block.Metadata!["subtype"]);
        Assert.Equal("2", block.Metadata["hookCount"]);
        Assert.Equal("True", block.Metadata["preventedContinuation"]);
        Assert.Equal("blocked", block.Metadata["stopReason"]);
        Assert.Equal("True", block.Metadata["hasOutput"]);
        Assert.Equal("warning", block.Metadata["level"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("PostToolUse", block.Metadata["hookLabel"]);
        Assert.Equal("1234", block.Metadata["totalDurationMs"]);

        var hookInfos = JsonSerializer.Deserialize<StopHookInfo[]>(block.Metadata["hookInfos"]);
        Assert.NotNull(hookInfos);
        Assert.Equal(2, hookInfos.Length);
        Assert.Equal("git diff", hookInfos[0].Command);
        Assert.Equal("check workspace", hookInfos[0].PromptText);

        var hookErrors = JsonSerializer.Deserialize<string[]>(block.Metadata["hookErrors"]);
        Assert.NotNull(hookErrors);
        Assert.Equal(["hook failed"], hookErrors);
    }

    [Fact]
    public void CreateTurnDurationMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateTurnDurationMessage(
            durationMs: 9876,
            budget: new TurnDurationBudget(100, 200, 3),
            messageCount: 5);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Text, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("turn_duration", block.Metadata!["subtype"]);
        Assert.Equal("9876", block.Metadata["durationMs"]);
        Assert.Equal("100", block.Metadata["budgetTokens"]);
        Assert.Equal("200", block.Metadata["budgetLimit"]);
        Assert.Equal("3", block.Metadata["budgetNudges"]);
        Assert.Equal("5", block.Metadata["messageCount"]);
        Assert.Equal("False", block.Metadata["isMeta"]);
    }
}
