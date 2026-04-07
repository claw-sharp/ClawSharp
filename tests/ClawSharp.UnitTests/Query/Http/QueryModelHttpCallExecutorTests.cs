// TS parity status: focused coverage for the HTTP-backed model-call executor seam; live auth resolution and concrete streamed-event parsing remain intentionally delegated to injected contracts.
using System.Text.Json.Nodes;
using System.Net;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryModelHttpCallExecutorTests
{
    [Fact]
    public async Task StreamAsync_Uses_Config_Provider_And_Parses_Raw_Stream_Payloads()
    {
        var configProvider = new CapturingConfigProvider();
        var streamingClient = new CapturingStreamingClient();
        var parser = new FakeStreamUpdateParser();
        var executor = new QueryModelHttpCallExecutor(configProvider, streamingClient, parser);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-executor", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "foundation-placeholder",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.NotNull(configProvider.CapturedConfigRequest);
        Assert.Equal("https://api.anthropic.test", streamingClient.CapturedConfig!.BaseUrl);
        Assert.Same(streamingRequest, streamingClient.CapturedStreamingRequest);
        Assert.Equal(3, updates.Count);
        Assert.IsType<QueryStreamDeltaRuntimeEvent>(updates[0].RuntimeEvent);
        Assert.IsType<QueryMessageRuntimeEvent>(updates[1].RuntimeEvent);
        Assert.NotNull(updates[2].AttemptResult);
    }

    [Fact]
    public async Task StreamAsync_Returns_FallbackRequested_After_Three_Overloaded_Attempts()
    {
        var configProvider = new CapturingConfigProvider();
        var streamingClient = new OverloadedStreamingClient();
        var parser = new FakeStreamUpdateParser();
        var executor = new QueryModelHttpCallExecutor(configProvider, streamingClient, parser);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-fallback", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            FallbackModel = "fallback-model"
        };
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(3, streamingClient.AttemptCount);
        Assert.Equal(3, updates.Count);
        Assert.All(
            updates.Take(2),
            update => Assert.IsType<QueryMessageRuntimeEvent>(update.RuntimeEvent));
        var fallbackUpdate = updates[2];
        Assert.NotNull(fallbackUpdate.AttemptResult);
        Assert.Equal(QueryModelCallAttemptOutcome.FallbackRequested, fallbackUpdate.AttemptResult!.Outcome);
        Assert.Equal("primary-model", fallbackUpdate.AttemptResult.OriginalModel);
        Assert.Equal("fallback-model", fallbackUpdate.AttemptResult.FallbackModel);
    }

    [Fact]
    public async Task StreamAsync_Does_Not_Retry_Overloaded_For_Background_Query_Source()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-background-overloaded", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "background_summary");

        var streamingClient = new OverloadedStreamingClient();
        var backgroundExecutor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser());

        var exception = await Assert.ThrowsAsync<QueryModelOverloadedException>(async () =>
        {
            await foreach (var _ in backgroundExecutor.StreamAsync(
                               streamingRequest,
                               request,
                               state,
                               session,
                               new ClawSharpSettings()))
            {
            }
        });

        Assert.Equal(1, streamingClient.AttemptCount);
        Assert.Equal((System.Net.HttpStatusCode)529, exception.StatusCode);
    }

    [Fact]
    public async Task StreamAsync_Retries_Server_Error_And_Completes()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-server-retry", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var streamingClient = new RetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.InternalServerError,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"type":"api_error"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, streamingClient.AttemptCount);
        Assert.Equal(4, updates.Count);
        var retryEvent = Assert.IsType<QueryMessageRuntimeEvent>(updates[0].RuntimeEvent);
        Assert.Equal(MessageRole.System, retryEvent.Message.Role);
        Assert.Equal("api_error", retryEvent.Message.ContentBlocks[0].Metadata!["subtype"]);
        Assert.NotNull(updates[^1].AttemptResult);
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Respects_XShouldRetry_False_For_NonAnt_5xx()
    {
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        Environment.SetEnvironmentVariable("USER_TYPE", "external");

        try
        {
            var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            var session = new ConversationSession("session-http-x-should-retry-false", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
            var request = QueryTurnRequest.Create(session, "hello");
            var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
            var streamingRequest = new QueryModelHttpStreamingRequest(
                new QueryModelRequest(
                    request.SessionId,
                    "primary-model",
                    [new QuerySystemPromptBlock("system")],
                    [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                    [],
                    new QueryRequestOutputConfig(),
                    []),
                "repl_main_thread");

            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-should-retry"] = ["false"]
            };

            var streamingClient = new RetryThenSuccessStreamingClient(
                new QueryModelApiException(
                    HttpStatusCode.InternalServerError,
                    headers,
                    """{"type":"error","error":{"type":"api_error"}}"""));

            var executor = new QueryModelHttpCallExecutor(
                new CapturingConfigProvider(),
                streamingClient,
                new FakeStreamUpdateParser(),
                new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

            var exception = await Assert.ThrowsAsync<QueryModelApiException>(async () =>
            {
                await foreach (var _ in executor.StreamAsync(
                                   streamingRequest,
                                   request,
                                   state,
                                   session,
                                   new ClawSharpSettings()))
                {
                }
            });

            Assert.Equal(1, streamingClient.AttemptCount);
            Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
        }
    }

    [Fact]
    public async Task StreamAsync_Retries_XShouldRetry_False_For_Remote_403()
    {
        var originalRemote = Environment.GetEnvironmentVariable("CLAUDE_CODE_REMOTE");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_REMOTE", "true");

        try
        {
            var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            var session = new ConversationSession("session-http-remote-403-retry", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
            var request = QueryTurnRequest.Create(session, "hello");
            var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
            var streamingRequest = new QueryModelHttpStreamingRequest(
                new QueryModelRequest(
                    request.SessionId,
                    "primary-model",
                    [new QuerySystemPromptBlock("system")],
                    [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                    [],
                    new QueryRequestOutputConfig(),
                    []),
                "repl_main_thread");

            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-should-retry"] = ["false"]
            };

            var streamingClient = new RetryThenSuccessStreamingClient(
                new QueryModelApiException(
                    HttpStatusCode.Forbidden,
                    headers,
                    """{"type":"error","error":{"type":"permission_error"}}"""));

            var executor = new QueryModelHttpCallExecutor(
                new CapturingConfigProvider(),
                streamingClient,
                new FakeStreamUpdateParser(),
                new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

            var updates = new List<QueryModelCallUpdate>();
            await foreach (var update in executor.StreamAsync(
                               streamingRequest,
                               request,
                               state,
                               session,
                               new ClawSharpSettings()))
            {
                updates.Add(update);
            }

            Assert.Equal(2, streamingClient.AttemptCount);
            Assert.IsType<QueryMessageRuntimeEvent>(updates[0].RuntimeEvent);
            Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_REMOTE", originalRemote);
        }
    }

    [Fact]
    public async Task StreamAsync_Retries_MaxTokens_Context_Overflow_With_Adjusted_MaxTokens()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-max-tokens-overflow", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 20_000),
            "repl_main_thread");

        var streamingClient = new CapturingRetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.BadRequest,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"type":"invalid_request_error","message":"input length and `max_tokens` exceed context limit: 188059 + 20000 > 200000"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, streamingClient.AttemptCount);
        Assert.Equal(20_000, streamingClient.CapturedRequests[0].Request.MaxTokens);
        Assert.Equal(10_941, streamingClient.CapturedRequests[1].Request.MaxTokens);
        Assert.DoesNotContain(
            updates,
            update => update.RuntimeEvent is QueryMessageRuntimeEvent runtimeEvent &&
                      runtimeEvent.Message.Role == MessageRole.System &&
                      runtimeEvent.Message.ContentBlocks[0].Metadata?["subtype"] == "api_error");
        Assert.NotNull(updates[^1].AttemptResult);
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Retries_429_For_NonSubscriber_Account_State()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-rate-limit-api", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var streamingClient = new RetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.TooManyRequests,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"type":"rate_limit_error"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, streamingClient.AttemptCount);
        Assert.IsType<QueryMessageRuntimeEvent>(updates[0].RuntimeEvent);
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Does_Not_Retry_429_For_NonEnterprise_Subscriber()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-rate-limit-subscriber", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var rateLimitException = new QueryModelApiException(
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            """{"type":"error","error":{"type":"rate_limit_error"}}""");
        var streamingClient = new RetryThenSuccessStreamingClient(rateLimitException);
        var executor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(new QueryAuthAccountState(true, false, "pro")));

        var exception = await Assert.ThrowsAsync<QueryModelApiException>(async () =>
        {
            await foreach (var _ in executor.StreamAsync(
                               streamingRequest,
                               request,
                               state,
                               session,
                               new ClawSharpSettings()))
            {
            }
        });

        Assert.Equal(1, streamingClient.AttemptCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    [Fact]
    public async Task StreamAsync_Retries_XShouldRetry_True_For_Enterprise_Subscriber()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-header-true-enterprise", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var retryHeaders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-should-retry"] = ["true"]
        };
        var streamingClient = new RetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.BadRequest,
                retryHeaders,
                """{"type":"error","error":{"type":"temporary_error"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            new CapturingConfigProvider(),
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(new QueryAuthAccountState(true, true, "enterprise")));

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, streamingClient.AttemptCount);
        Assert.IsType<QueryMessageRuntimeEvent>(updates[0].RuntimeEvent);
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Retries_429_Beyond_MaxRetries_When_Persistent_Retry_Is_Enabled()
    {
        var originalPersistent = Environment.GetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY");
        var originalMaxRetries = Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY", "true");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES", "0");

        try
        {
            var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            var session = new ConversationSession("session-http-persistent-429", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
            var request = QueryTurnRequest.Create(session, "hello");
            var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
            var streamingRequest = new QueryModelHttpStreamingRequest(
                new QueryModelRequest(
                    request.SessionId,
                    "primary-model",
                    [new QuerySystemPromptBlock("system")],
                    [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                    [],
                    new QueryRequestOutputConfig(),
                    []),
                "repl_main_thread");

            var retryHeaders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["retry-after"] = ["0"]
            };
            var streamingClient = new RetryThenSuccessStreamingClient(
                new QueryModelApiException(
                    HttpStatusCode.TooManyRequests,
                    retryHeaders,
                    """{"type":"error","error":{"type":"rate_limit_error"}}"""));

            var executor = new QueryModelHttpCallExecutor(
                new CapturingConfigProvider(),
                streamingClient,
                new FakeStreamUpdateParser(),
                new FixedAuthAccountStateProvider(new QueryAuthAccountState(true, false, "pro")));

            var updates = new List<QueryModelCallUpdate>();
            await foreach (var update in executor.StreamAsync(
                               streamingRequest,
                               request,
                               state,
                               session,
                               new ClawSharpSettings()))
            {
                updates.Add(update);
            }

            Assert.Equal(2, streamingClient.AttemptCount);
            Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY", originalPersistent);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES", originalMaxRetries);
        }
    }

    [Fact]
    public async Task StreamAsync_Emits_ApiRetry_Heartbeat_During_Persistent_Retry_Wait()
    {
        var originalPersistent = Environment.GetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY");
        var originalMaxRetries = Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY", "true");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES", "0");

        try
        {
            var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            var session = new ConversationSession("session-http-persistent-heartbeat", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
            var request = QueryTurnRequest.Create(session, "hello");
            var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
            var streamingRequest = new QueryModelHttpStreamingRequest(
                new QueryModelRequest(
                    request.SessionId,
                    "primary-model",
                    [new QuerySystemPromptBlock("system")],
                    [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                    [],
                    new QueryRequestOutputConfig(),
                    []),
                "repl_main_thread");

            var retryHeaders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["retry-after"] = ["1"]
            };
            var streamingClient = new RetryThenSuccessStreamingClient(
                new QueryModelApiException(
                    HttpStatusCode.TooManyRequests,
                    retryHeaders,
                    """{"type":"error","error":{"type":"rate_limit_error"}}"""));

            var executor = new QueryModelHttpCallExecutor(
                new CapturingConfigProvider(),
                streamingClient,
                new FakeStreamUpdateParser(),
                new FixedAuthAccountStateProvider(new QueryAuthAccountState(true, false, "pro")));

            var updates = new List<QueryModelCallUpdate>();
            await foreach (var update in executor.StreamAsync(
                               streamingRequest,
                               request,
                               state,
                               session,
                               new ClawSharpSettings()))
            {
                updates.Add(update);
            }

            var retryEvent = Assert.IsType<QueryMessageRuntimeEvent>(updates[0].RuntimeEvent);
            Assert.Equal(MessageRole.System, retryEvent.Message.Role);
            Assert.Equal("api_error", retryEvent.Message.ContentBlocks[0].Metadata!["subtype"]);
            Assert.Equal("1000", retryEvent.Message.ContentBlocks[0].Metadata!["retryInMs"]);
            Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY", originalPersistent);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES", originalMaxRetries);
        }
    }

    [Fact]
    public async Task StreamAsync_Requeries_Config_After_401_Retry()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-401-refresh", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var configProvider = new RotatingConfigProvider(
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "stale-token"),
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "fresh-token"));
        var authFailureRecoveryRunner = new CapturingAuthFailureRecoveryRunner();
        var streamingClient = new CapturingConfigRetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.Unauthorized,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"type":"authentication_error"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            configProvider,
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None),
            authFailureRecoveryRunner);

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, configProvider.CallCount);
        Assert.Single(authFailureRecoveryRunner.CapturedStatuses);
        Assert.Equal(HttpStatusCode.Unauthorized, authFailureRecoveryRunner.CapturedStatuses[0]);
        Assert.Collection(
            streamingClient.CapturedConfigs,
            config => Assert.Equal("stale-token", config.AuthToken),
            config => Assert.Equal("fresh-token", config.AuthToken));
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Requeries_Config_After_Revoked_OAuth_403_Retry()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-403-refresh", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var configProvider = new RotatingConfigProvider(
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "revoked-token"),
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "replacement-token"));
        var authFailureRecoveryRunner = new CapturingAuthFailureRecoveryRunner();
        var streamingClient = new CapturingConfigRetryThenSuccessStreamingClient(
            new QueryModelApiException(
                HttpStatusCode.Forbidden,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"message":"OAuth token has been revoked"}}"""));

        var executor = new QueryModelHttpCallExecutor(
            configProvider,
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None),
            authFailureRecoveryRunner);

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, configProvider.CallCount);
        Assert.Single(authFailureRecoveryRunner.CapturedStatuses);
        Assert.Equal(HttpStatusCode.Forbidden, authFailureRecoveryRunner.CapturedStatuses[0]);
        Assert.Collection(
            streamingClient.CapturedConfigs,
            config => Assert.Equal("revoked-token", config.AuthToken),
            config => Assert.Equal("replacement-token", config.AuthToken));
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    [Fact]
    public async Task StreamAsync_Requeries_Config_After_Stale_Connection_Retry()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-http-stale-connection-refresh", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                request.SessionId,
                "primary-model",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");

        var configProvider = new RotatingConfigProvider(
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "socket-one"),
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "socket-two"));
        var streamingClient = new CapturingConfigRetryThenSuccessStreamingClient(
            new HttpRequestException("socket closed with ECONNRESET"));

        var executor = new QueryModelHttpCallExecutor(
            configProvider,
            streamingClient,
            new FakeStreamUpdateParser(),
            new FixedAuthAccountStateProvider(QueryAuthAccountState.None));

        var updates = new List<QueryModelCallUpdate>();
        await foreach (var update in executor.StreamAsync(
                           streamingRequest,
                           request,
                           state,
                           session,
                           new ClawSharpSettings()))
        {
            updates.Add(update);
        }

        Assert.Equal(2, configProvider.CallCount);
        Assert.Collection(
            streamingClient.CapturedConfigs,
            config => Assert.Equal("socket-one", config.AuthToken),
            config => Assert.Equal("socket-two", config.AuthToken));
        Assert.Equal(QueryModelCallAttemptOutcome.Completed, updates[^1].AttemptResult!.Outcome);
    }

    private sealed class CapturingConfigProvider : IQueryModelHttpClientConfigProvider
    {
        public QueryTurnRequest? CapturedConfigRequest { get; private set; }

        public QueryModelHttpClientConfig GetConfig(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings)
        {
            CapturedConfigRequest = request;
            return new QueryModelHttpClientConfig("https://api.anthropic.test", "test-key");
        }
    }

    private sealed class RotatingConfigProvider : IQueryModelHttpClientConfigProvider
    {
        private readonly Queue<QueryModelHttpClientConfig> _configs;

        public RotatingConfigProvider(params QueryModelHttpClientConfig[] configs)
        {
            _configs = new Queue<QueryModelHttpClientConfig>(configs);
        }

        public int CallCount { get; private set; }

        public QueryModelHttpClientConfig GetConfig(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings)
        {
            CallCount++;
            if (_configs.Count > 1)
            {
                return _configs.Dequeue();
            }

            return _configs.Peek();
        }
    }

    private sealed class CapturingStreamingClient : IQueryModelHttpStreamingClient
    {
        public QueryModelHttpClientConfig? CapturedConfig { get; private set; }
        public QueryModelHttpStreamingRequest? CapturedStreamingRequest { get; private set; }

        public async IAsyncEnumerable<JsonNode> StreamAsync(
            QueryModelHttpClientConfig config,
            QueryModelHttpStreamingRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedConfig = config;
            CapturedStreamingRequest = request;
            yield return new JsonObject { ["type"] = "content_block_delta", ["text"] = "hello" };
            yield return new JsonObject { ["type"] = "message_stop", ["content"] = "done" };
            await Task.CompletedTask;
        }
    }

    private sealed class FakeStreamUpdateParser : IQueryModelStreamUpdateParser
    {
        public void Reset()
        {
        }

        public IReadOnlyList<QueryModelCallUpdate> Parse(JsonNode payload)
        {
            return payload["type"]?.GetValue<string>() switch
            {
                "content_block_delta" =>
                [
                    new QueryModelCallUpdate(
                        new QueryStreamDeltaRuntimeEvent(payload["text"]?.GetValue<string>() ?? string.Empty))
                ],
                "message_stop" =>
                [
                    new QueryModelCallUpdate(
                        new QueryMessageRuntimeEvent(
                            ChatMessageFactory.CreateText(
                                MessageRole.Assistant,
                                payload["content"]?.GetValue<string>() ?? string.Empty)))
                ],
                _ => []
            };
        }

        public IReadOnlyList<QueryModelCallUpdate> Complete(QueryLoopState state)
        {
            return
            [
                new QueryModelCallUpdate(
                    AttemptResult: QueryModelCallAttemptResult.Completed(
                        new QueryTerminalIterationResult(
                            new QueryLoopTerminal(QueryTerminalReason.Completed),
                            state)))
            ];
        }
    }

    private sealed class OverloadedStreamingClient : IQueryModelHttpStreamingClient
    {
        public int AttemptCount { get; private set; }

        public async IAsyncEnumerable<JsonNode> StreamAsync(
            QueryModelHttpClientConfig config,
            QueryModelHttpStreamingRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            await Task.Yield();
            throw new QueryModelOverloadedException(
                (System.Net.HttpStatusCode)529,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                """{"type":"error","error":{"type":"overloaded_error"}}""");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class RetryThenSuccessStreamingClient : IQueryModelHttpStreamingClient
    {
        private readonly Exception _firstException;

        public RetryThenSuccessStreamingClient(Exception firstException)
        {
            _firstException = firstException;
        }

        public int AttemptCount { get; private set; }

        public async IAsyncEnumerable<JsonNode> StreamAsync(
            QueryModelHttpClientConfig config,
            QueryModelHttpStreamingRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            await Task.Yield();
            if (AttemptCount == 1)
            {
                throw _firstException;
            }

            yield return new JsonObject { ["type"] = "content_block_delta", ["text"] = "hello" };
            yield return new JsonObject { ["type"] = "message_stop", ["content"] = "done" };
        }
    }

    private sealed class FixedAuthAccountStateProvider : IQueryAuthAccountStateProvider
    {
        private readonly QueryAuthAccountState _state;

        public FixedAuthAccountStateProvider(QueryAuthAccountState state)
        {
            _state = state;
        }

        public QueryAuthAccountState GetState(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings)
        {
            return _state;
        }
    }

    private sealed class CapturingAuthFailureRecoveryRunner : IQueryAuthFailureRecoveryRunner
    {
        public List<HttpStatusCode> CapturedStatuses { get; } = [];

        public Task RunAsync(
            QueryModelApiException exception,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            CancellationToken cancellationToken = default)
        {
            CapturedStatuses.Add(exception.StatusCode);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRetryThenSuccessStreamingClient : IQueryModelHttpStreamingClient
    {
        private readonly Exception _firstException;

        public CapturingRetryThenSuccessStreamingClient(Exception firstException)
        {
            _firstException = firstException;
        }

        public int AttemptCount { get; private set; }

        public List<QueryModelHttpStreamingRequest> CapturedRequests { get; } = [];

        public async IAsyncEnumerable<JsonNode> StreamAsync(
            QueryModelHttpClientConfig config,
            QueryModelHttpStreamingRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            CapturedRequests.Add(request);
            await Task.Yield();
            if (AttemptCount == 1)
            {
                throw _firstException;
            }

            yield return new JsonObject { ["type"] = "content_block_delta", ["text"] = "hello" };
            yield return new JsonObject { ["type"] = "message_stop", ["content"] = "done" };
        }
    }

    private sealed class CapturingConfigRetryThenSuccessStreamingClient : IQueryModelHttpStreamingClient
    {
        private readonly Exception _firstException;

        public CapturingConfigRetryThenSuccessStreamingClient(Exception firstException)
        {
            _firstException = firstException;
        }

        public int AttemptCount { get; private set; }

        public List<QueryModelHttpClientConfig> CapturedConfigs { get; } = [];

        public async IAsyncEnumerable<JsonNode> StreamAsync(
            QueryModelHttpClientConfig config,
            QueryModelHttpStreamingRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            CapturedConfigs.Add(config);
            await Task.Yield();
            if (AttemptCount == 1)
            {
                throw _firstException;
            }

            yield return new JsonObject { ["type"] = "content_block_delta", ["text"] = "hello" };
            yield return new JsonObject { ["type"] = "message_stop", ["content"] = "done" };
        }
    }
}
