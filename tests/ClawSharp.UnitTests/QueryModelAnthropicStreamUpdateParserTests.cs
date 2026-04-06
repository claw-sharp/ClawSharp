// TS origin: ./services/api/claude.ts, ./query.ts
// TS parity status: focused coverage for the concrete Anthropic model-stream parser beneath the C# model-call executor, including per-content-block assistant emission and raw stream-event ordering; fallback retry and model-side continuation behavior remain intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryModelAnthropicStreamUpdateParserTests
{
    [Fact]
    public void Parse_Emits_Text_Delta_And_ContentBlockStop_Assistant_Message_Before_Stream_Event()
    {
        var parser = new QueryModelAnthropicStreamUpdateParser();
        parser.Reset();

        var updates = new List<QueryModelCallUpdate>();
        updates.AddRange(parser.Parse(new JsonObject { ["type"] = "message_start" }));
        updates.AddRange(parser.Parse(new JsonObject
        {
            ["type"] = "content_block_start",
            ["index"] = 0,
            ["content_block"] = new JsonObject
            {
                ["type"] = "text",
                ["text"] = "prefilled text that TS ignores"
            }
        }));
        var deltaUpdates = parser.Parse(new JsonObject
        {
            ["type"] = "content_block_delta",
            ["index"] = 0,
            ["delta"] = new JsonObject
            {
                ["type"] = "text_delta",
                ["text"] = "Hello"
            }
        });
        var contentBlockStopUpdates = parser.Parse(new JsonObject
        {
            ["type"] = "content_block_stop",
            ["index"] = 0
        });
        updates.AddRange(deltaUpdates);
        updates.AddRange(contentBlockStopUpdates);
        updates.AddRange(parser.Parse(new JsonObject
        {
            ["type"] = "message_delta",
            ["usage"] = new JsonObject
            {
                ["output_tokens"] = 321
            }
        }));
        updates.AddRange(parser.Parse(new JsonObject { ["type"] = "message_stop" }));

        var completion = parser.Complete(QueryLoopStateFactory.CreateInitial([]));

        Assert.Collection(
            deltaUpdates,
            update =>
            {
                var delta = Assert.IsType<QueryStreamDeltaRuntimeEvent>(update.RuntimeEvent);
                Assert.Equal("Hello", delta.Delta);
            },
            update => Assert.IsType<QueryStreamEventRuntimeEvent>(update.RuntimeEvent));
        Assert.Collection(
            contentBlockStopUpdates,
            update =>
            {
                var assistantMessage = Assert.IsType<QueryMessageRuntimeEvent>(update.RuntimeEvent).Message;
                Assert.Equal(MessageRole.Assistant, assistantMessage.Role);
                Assert.Equal("Hello", assistantMessage.Content);
            },
            update => Assert.IsType<QueryStreamEventRuntimeEvent>(update.RuntimeEvent));
        Assert.DoesNotContain(completion, update => update.RuntimeEvent is QueryMessageRuntimeEvent);

        var assistantMessage = Assert.IsType<QueryMessageRuntimeEvent>(contentBlockStopUpdates[0].RuntimeEvent).Message;
        Assert.Equal(MessageRole.Assistant, assistantMessage.Role);
        Assert.Equal("Hello", assistantMessage.Content);

        var attemptResult = Assert.Single(completion, update => update.AttemptResult is not null).AttemptResult;
        Assert.NotNull(attemptResult);
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, attemptResult!.Outcome);
        Assert.Equal(321, attemptResult.TurnOutputTokens);
        var terminal = Assert.IsType<QueryTerminalIterationResult>(attemptResult.IterationResult);
        Assert.Equal("Hello", terminal.State.Messages[^1].Content);
    }

    [Fact]
    public void Parse_And_Complete_Accumulate_ToolUse_Content_Block_On_ContentBlockStop()
    {
        var parser = new QueryModelAnthropicStreamUpdateParser();
        parser.Reset();

        parser.Parse(new JsonObject
        {
            ["type"] = "content_block_start",
            ["index"] = 0,
            ["content_block"] = new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = "toolu_1",
                ["name"] = "Read",
                ["input"] = new JsonObject()
            }
        });
        parser.Parse(new JsonObject
        {
            ["type"] = "content_block_delta",
            ["index"] = 0,
            ["delta"] = new JsonObject
            {
                ["type"] = "input_json_delta",
                ["partial_json"] = "{\"path\":\"note.txt\"}"
            }
        });
        var stopUpdates = parser.Parse(new JsonObject
        {
            ["type"] = "content_block_stop",
            ["index"] = 0
        });
        parser.Parse(new JsonObject { ["type"] = "message_stop" });

        var completion = parser.Complete(QueryLoopStateFactory.CreateInitial([]));

        Assert.DoesNotContain(completion, update => update.RuntimeEvent is QueryMessageRuntimeEvent);
        var assistantMessage = Assert.IsType<QueryMessageRuntimeEvent>(
            Assert.Single(stopUpdates, update => update.RuntimeEvent is QueryMessageRuntimeEvent).RuntimeEvent).Message;
        var block = Assert.Single(assistantMessage.ContentBlocks);
        Assert.Equal(MessageContentKind.ToolUse, block.Kind);
        Assert.Equal("Read", block.Name);
        Assert.Equal("toolu_1", block.Metadata!["toolUseId"]);
        Assert.Equal("{\"path\":\"note.txt\"}", block.Value);
    }

    [Fact]
    public void Parse_Error_Emits_Assistant_Api_Error_Message_And_Complete_Returns_Terminal_Result()
    {
        var parser = new QueryModelAnthropicStreamUpdateParser();
        parser.Reset();

        var updates = parser.Parse(new JsonObject
        {
            ["type"] = "error",
            ["error"] = new JsonObject
            {
                ["type"] = "overloaded_error",
                ["message"] = "Rate limited"
            }
        });
        var completion = parser.Complete(QueryLoopStateFactory.CreateInitial([]));

        var apiErrorMessage = Assert.IsType<QueryMessageRuntimeEvent>(
            Assert.Single(updates, update => update.RuntimeEvent is QueryMessageRuntimeEvent).RuntimeEvent).Message;
        var block = Assert.Single(apiErrorMessage.ContentBlocks);
        Assert.Equal("Rate limited", block.Value);
        Assert.Equal("overloaded_error", block.Metadata!["apiError"]);
        Assert.Equal("True", block.Metadata["isApiErrorMessage"]);

        var attemptResult = Assert.Single(completion, update => update.AttemptResult is not null).AttemptResult;
        Assert.NotNull(attemptResult);
        var terminal = Assert.IsType<QueryTerminalIterationResult>(attemptResult!.IterationResult);
        Assert.Equal("Rate limited", terminal.State.Messages[^1].Content);
    }

    [Fact]
    public void Parse_MaxOutputTokens_Error_Withholds_Runtime_Message_But_Preserves_State_Message()
    {
        var parser = new QueryModelAnthropicStreamUpdateParser();
        parser.Reset();

        var updates = parser.Parse(new JsonObject
        {
            ["type"] = "error",
            ["error"] = new JsonObject
            {
                ["type"] = "max_output_tokens",
                ["message"] = "max tokens"
            }
        });
        var completion = parser.Complete(QueryLoopStateFactory.CreateInitial([]));

        Assert.DoesNotContain(updates, update => update.RuntimeEvent is QueryMessageRuntimeEvent);
        var attemptResult = Assert.Single(completion, update => update.AttemptResult is not null).AttemptResult;
        Assert.NotNull(attemptResult);
        var terminal = Assert.IsType<QueryTerminalIterationResult>(attemptResult!.IterationResult);
        Assert.Equal("max tokens", terminal.State.Messages[^1].Content);
    }

    [Fact]
    public void Parse_ContentBlockDelta_Throws_When_Block_Has_Not_Been_Started()
    {
        var parser = new QueryModelAnthropicStreamUpdateParser();
        parser.Reset();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            parser.Parse(new JsonObject
            {
                ["type"] = "content_block_delta",
                ["index"] = 0,
                ["delta"] = new JsonObject
                {
                    ["type"] = "text_delta",
                    ["text"] = "Hello"
                }
            }));

        Assert.Equal("Content block not found.", exception.Message);
    }
}
