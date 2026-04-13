// TS parity status: ports the current request-construction foundation for system prompt splitting, user/system context shaping, cache-marker placement, task-budget shaping, and tool schema projection; live model transport, thinking blocks, and provider-specific beta handling remain blocked.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClawSharp.Core;
using ClawSharp.Query.Attachments;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public sealed class QueryRequestBuilder
{
    public const string SystemPromptDynamicBoundary = "__SYSTEM_PROMPT_DYNAMIC_BOUNDARY__";
    public const string TaskBudgetsBetaHeader = "task-budgets-2026-03-13";

    private static readonly HashSet<string> CliSystemPromptPrefixes =
    [
        "You are Claude Code, Anthropic's official CLI for Claude.",
        "You are Claude Code, Anthropic's official CLI for Claude, running within the Claude Agent SDK.",
        "You are a Claude agent, built on Anthropic's Claude Agent SDK."
    ];

    public QueryModelRequest Build(
        QueryTurnRequest request,
        ConversationSession session,
        ClawSharpSettings settings,
        IReadOnlyList<ToolDescriptor> availableTools,
        QueryRequestBuildOptions? options = null)
    {
        return BuildFromMessages(
            request,
            session.Messages,
            settings,
            availableTools,
            options);
    }

    public QueryModelRequest BuildFromMessages(
        QueryTurnRequest request,
        IReadOnlyList<ChatMessage> messages,
        ClawSharpSettings settings,
        IReadOnlyList<ToolDescriptor> availableTools,
        QueryRequestBuildOptions? options = null)
    {
        options ??= new QueryRequestBuildOptions();

        var systemPrompt = options.SystemPrompt ?? [];
        if (options.SystemContext is { Count: > 0 })
        {
            systemPrompt = AppendSystemContext(systemPrompt, options.SystemContext);
        }

        var requestMessages = messages;
        if (options.UserContext is { Count: > 0 })
        {
            requestMessages = PrependUserContext(
                requestMessages,
                options.UserContext,
                options.IncludeUserContextInTestEnvironment);
        }

        var outputConfig = new QueryRequestOutputConfig();
        var betas = new List<string>();
        ConfigureTaskBudget(
            options.TaskBudget,
            ref outputConfig,
            betas,
            options.ShouldIncludeFirstPartyOnlyBetas);

        var resolvedModel = MainLoopModelResolver.Resolve(settings.Runtime.Model);

        return new QueryModelRequest(
            request.SessionId,
            resolvedModel,
            BuildSystemPromptBlocks(
                systemPrompt,
                options.EnablePromptCaching,
                options.UseGlobalCacheScope,
                options.SkipGlobalCacheForSystemPrompt),
            AddCacheBreakpoints(
                request,
                requestMessages,
                options.EnablePromptCaching,
                options.SkipCacheWrite),
            availableTools.Select(ToRequestTool).ToArray(),
            outputConfig,
            betas,
            MaxTokens: QueryMaxOutputTokensResolver.GetMaxOutputTokensForModel(resolvedModel));
    }

    public static IReadOnlyList<string> AppendSystemContext(
        IReadOnlyList<string> systemPrompt,
        IReadOnlyDictionary<string, string> context)
    {
        return systemPrompt
            .Concat([string.Join('\n', context.Select(pair => $"{pair.Key}: {pair.Value}"))])
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToArray();
    }

    public static IReadOnlyList<ChatMessage> PrependUserContext(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyDictionary<string, string> context,
        bool includeInTestEnvironment = false)
    {
        if (context.Count == 0)
        {
            return messages.ToArray();
        }

        if (!includeInTestEnvironment &&
            string.Equals(
                Environment.GetEnvironmentVariable("NODE_ENV"),
                "test",
                StringComparison.OrdinalIgnoreCase))
        {
            return messages.ToArray();
        }

        var reminder = ChatMessageFactory.CreateText(
            MessageRole.User,
            "<system-reminder>\nAs you answer the user's questions, you can use the following context:\n" +
            string.Join('\n', context.Select(pair => $"# {pair.Key}\n{pair.Value}")) +
            "\n\nIMPORTANT: this context may or may not be relevant to your tasks. You should not respond to this context unless it is highly relevant to your task.\n</system-reminder>\n");

        return (new[] { reminder })
            .Concat(messages)
            .ToArray();
    }

    public static IReadOnlyList<QuerySystemPromptBlock> BuildSystemPromptBlocks(
        IReadOnlyList<string> systemPrompt,
        bool enablePromptCaching,
        bool useGlobalCacheScope,
        bool skipGlobalCacheForSystemPrompt = false)
    {
        return SplitSystemPromptPrefix(systemPrompt, useGlobalCacheScope, skipGlobalCacheForSystemPrompt)
            .Select(
                block =>
                    block with
                    {
                        CacheControl =
                            enablePromptCaching && block.CacheScope is not null
                                ? new QueryRequestCacheControl("ephemeral", block.CacheScope)
                                : null
                    })
            .ToArray();
    }

    public static IReadOnlyList<QuerySystemPromptBlock> SplitSystemPromptPrefix(
        IReadOnlyList<string> systemPrompt,
        bool useGlobalCacheScope,
        bool skipGlobalCacheForSystemPrompt = false)
    {
        if (useGlobalCacheScope && skipGlobalCacheForSystemPrompt)
        {
            return SplitWithoutGlobalCache(systemPrompt);
        }

        if (useGlobalCacheScope)
        {
            var boundaryIndex = systemPrompt
                .Select((block, index) => new { block, index })
                .FirstOrDefault(pair => string.Equals(pair.block, SystemPromptDynamicBoundary, StringComparison.Ordinal))
                ?.index ?? -1;

            if (boundaryIndex != -1)
            {
                string? attributionHeader = null;
                string? systemPromptPrefix = null;
                List<string> staticBlocks = [];
                List<string> dynamicBlocks = [];

                for (var index = 0; index < systemPrompt.Count; index++)
                {
                    var block = systemPrompt[index];
                    if (string.IsNullOrWhiteSpace(block) ||
                        string.Equals(block, SystemPromptDynamicBoundary, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsAttributionHeader(block))
                    {
                        attributionHeader = block;
                    }
                    else if (CliSystemPromptPrefixes.Contains(block))
                    {
                        systemPromptPrefix = block;
                    }
                    else if (index < boundaryIndex)
                    {
                        staticBlocks.Add(block);
                    }
                    else
                    {
                        dynamicBlocks.Add(block);
                    }
                }

                List<QuerySystemPromptBlock> result = [];
                if (attributionHeader is not null)
                {
                    result.Add(new QuerySystemPromptBlock(attributionHeader));
                }

                if (systemPromptPrefix is not null)
                {
                    result.Add(new QuerySystemPromptBlock(systemPromptPrefix));
                }

                var staticJoined = string.Join("\n\n", staticBlocks);
                if (!string.IsNullOrWhiteSpace(staticJoined))
                {
                    result.Add(new QuerySystemPromptBlock(staticJoined, "global"));
                }

                var dynamicJoined = string.Join("\n\n", dynamicBlocks);
                if (!string.IsNullOrWhiteSpace(dynamicJoined))
                {
                    result.Add(new QuerySystemPromptBlock(dynamicJoined));
                }

                return result;
            }
        }

        return SplitWithoutGlobalCache(systemPrompt);
    }

    public static IReadOnlyList<QueryRequestMessage> AddCacheBreakpoints(
        QueryTurnRequest request,
        IReadOnlyList<ChatMessage> messages,
        bool enablePromptCaching,
        bool skipCacheWrite = false)
    {
        var apiMessages = messages
            .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
            .ToArray();

        if (apiMessages.Length == 0)
        {
            return [];
        }

        var markerIndex = skipCacheWrite ? apiMessages.Length - 2 : apiMessages.Length - 1;
        if (markerIndex < 0)
        {
            markerIndex = apiMessages.Length - 1;
        }

        var lastUserMessageIndex = Array.FindLastIndex(apiMessages, static message => message.Role == MessageRole.User);

        return apiMessages
            .Select(
                (message, index) =>
                {
                    var shouldOverrideLastUserMessage = index == lastUserMessageIndex &&
                        (request.ResolvedUserInput is not null || request.EffectivePromptAttachments.Count > 0);

                    return message.Role == MessageRole.User
                        ? UserMessageToMessageParam(
                            message,
                            index == markerIndex,
                            enablePromptCaching,
                            overrideText: shouldOverrideLastUserMessage ? request.EffectiveUserInput : null,
                            attachments: shouldOverrideLastUserMessage ? request.EffectivePromptAttachments : [])
                        : AssistantMessageToMessageParam(message, index == markerIndex, enablePromptCaching);
                })
            .ToArray();
    }

    public static QueryRequestMessage UserMessageToMessageParam(
        ChatMessage message,
        bool addCache,
        bool enablePromptCaching,
        string? overrideText = null,
        IReadOnlyList<QueryPromptAttachment>? attachments = null)
    {
        if (overrideText is null &&
            TryBuildInlineAttachmentContent(message.Content, out var inlinePrompt, out var inlineAttachments))
        {
            return new QueryRequestMessage(
                "user",
                BuildContent(message, addCache, enablePromptCaching, inlinePrompt, inlineAttachments));
        }

        return new QueryRequestMessage(
            "user",
            BuildContent(message, addCache, enablePromptCaching, overrideText, attachments));
    }

    public static QueryRequestMessage AssistantMessageToMessageParam(
        ChatMessage message,
        bool addCache,
        bool enablePromptCaching)
    {
        return new QueryRequestMessage(
            "assistant",
            BuildContent(message, addCache, enablePromptCaching));
    }

    public static void ConfigureTaskBudget(
        QueryTaskBudget? taskBudget,
        ref QueryRequestOutputConfig outputConfig,
        IList<string> betas,
        bool shouldIncludeFirstPartyOnlyBetas)
    {
        if (taskBudget is null || outputConfig.TaskBudget is not null || !shouldIncludeFirstPartyOnlyBetas)
        {
            return;
        }

        outputConfig = outputConfig with { TaskBudget = taskBudget };
        if (!betas.Contains(TaskBudgetsBetaHeader, StringComparer.Ordinal))
        {
            betas.Add(TaskBudgetsBetaHeader);
        }
    }

    private static IReadOnlyList<QuerySystemPromptBlock> SplitWithoutGlobalCache(IReadOnlyList<string> systemPrompt)
    {
        string? attributionHeader = null;
        string? systemPromptPrefix = null;
        List<string> rest = [];

        foreach (var block in systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(block) ||
                string.Equals(block, SystemPromptDynamicBoundary, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsAttributionHeader(block))
            {
                attributionHeader = block;
            }
            else if (CliSystemPromptPrefixes.Contains(block))
            {
                systemPromptPrefix = block;
            }
            else
            {
                rest.Add(block);
            }
        }

        List<QuerySystemPromptBlock> result = [];
        if (attributionHeader is not null)
        {
            result.Add(new QuerySystemPromptBlock(attributionHeader));
        }

        if (systemPromptPrefix is not null)
        {
            result.Add(new QuerySystemPromptBlock(systemPromptPrefix, "org"));
        }

        var restJoined = string.Join("\n\n", rest);
        if (!string.IsNullOrWhiteSpace(restJoined))
        {
            result.Add(new QuerySystemPromptBlock(restJoined, "org"));
        }

        return result;
    }

    public static QueryRequestTool ToRequestTool(ToolDescriptor descriptor)
    {
        return new QueryRequestTool(
            descriptor.Name,
            descriptor.Description,
            descriptor.InputSchema?.DeepClone() as JsonObject,
            descriptor.Strict,
            descriptor.IsLongRunningCapable,
            descriptor.SearchHint,
            descriptor.ShouldDefer,
            descriptor.AlwaysLoad);
    }

    private static bool IsAttributionHeader(string block)
    {
        return block.StartsWith("x-anthropic-billing-header", StringComparison.Ordinal);
    }

    private static IReadOnlyList<QueryRequestContentBlock> BuildContent(
        ChatMessage message,
        bool addCache,
        bool enablePromptCaching,
        string? overrideText = null,
        IReadOnlyList<QueryPromptAttachment>? attachments = null)
    {
        var contentBlocks = overrideText is not null
            ? BuildPromptContentBlocks(overrideText, attachments)
            : message.ContentBlocks
                .Select(ToContentBlock)
                .Where(block => block is not null)
                .Cast<QueryRequestContentBlock>()
                .ToArray();

        var blocks = contentBlocks;

        if (!addCache || !enablePromptCaching || blocks.Length == 0)
        {
            return blocks;
        }

        var lastBlock = blocks[^1] with
        {
            CacheControl = new QueryRequestCacheControl("ephemeral")
        };

        blocks[^1] = lastBlock;
        return blocks;
    }

    private static QueryRequestContentBlock[] BuildPromptContentBlocks(
        string promptText,
        IReadOnlyList<QueryPromptAttachment>? attachments)
    {
        List<QueryRequestContentBlock> blocks = [];
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            blocks.Add(new QueryRequestContentBlock("text", Text: promptText));
        }
        else if ((attachments?.Count ?? 0) > 0)
        {
            blocks.Add(new QueryRequestContentBlock("text", Text: BuildAttachmentSummary(attachments!)));
        }

        foreach (var attachment in attachments ?? [])
        {
            if (attachment.Kind != QueryPromptAttachmentKind.Image ||
                string.IsNullOrWhiteSpace(attachment.Base64Data) ||
                string.IsNullOrWhiteSpace(attachment.MediaType))
            {
                continue;
            }

            blocks.Add(new QueryRequestContentBlock(
                "image",
                ImageSource: new QueryRequestImageSource("base64", attachment.MediaType, attachment.Base64Data)));
        }

        return blocks.ToArray();
    }

    private static string BuildAttachmentSummary(IReadOnlyList<QueryPromptAttachment> attachments)
    {
        var parts = attachments
            .Select(static attachment => attachment.Kind == QueryPromptAttachmentKind.Image
                ? $"attached image {attachment.Name}"
                : $"attached file {attachment.Name}")
            .ToArray();

        return $"Use the {string.Join(", ", parts)} as context for this turn.";
    }

    private static bool TryBuildInlineAttachmentContent(
        string rawContent,
        out string cleanedPrompt,
        out IReadOnlyList<QueryPromptAttachment> attachments)
    {
        cleanedPrompt = rawContent;
        attachments = [];

        var matches = PromptAttachmentMarkerPattern.Matches(rawContent);
        if (matches.Count == 0)
        {
            return false;
        }

        List<QueryPromptAttachment> parsedAttachments = [];
        foreach (Match match in matches)
        {
            var serialized = match.Groups["json"].Value;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(serialized) is not JsonObject parsed ||
                    string.IsNullOrWhiteSpace(parsed["path"]?.GetValue<string>()) ||
                    string.IsNullOrWhiteSpace(parsed["name"]?.GetValue<string>()))
                {
                    continue;
                }

                var kind = parsed["kind"]?.GetValue<string>();
                var path = parsed["path"]!.GetValue<string>();
                var name = parsed["name"]!.GetValue<string>();

                if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase) &&
                    TryReadInlineImageAttachment(path, name, out var imageAttachment))
                {
                    parsedAttachments.Add(imageAttachment);
                }
                else if (string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase))
                {
                    parsedAttachments.Add(new QueryPromptAttachment(QueryPromptAttachmentKind.File, path, name));
                }
            }
            catch
            {
                continue;
            }
        }

        cleanedPrompt = PromptAttachmentMarkerPattern.Replace(rawContent, string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        var fileAttachments = parsedAttachments
            .Where(static attachment => attachment.Kind == QueryPromptAttachmentKind.File)
            .ToArray();
        if (fileAttachments.Length > 0 && !string.IsNullOrWhiteSpace(cleanedPrompt))
        {
            cleanedPrompt = $"{cleanedPrompt}\n\n{BuildAttachmentSummary(fileAttachments)}";
        }

        attachments = parsedAttachments;
        return true;
    }

    private static bool TryReadInlineImageAttachment(string path, string name, out QueryPromptAttachment attachment)
    {
        attachment = new QueryPromptAttachment(QueryPromptAttachmentKind.Image, path, name);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch
        {
            return false;
        }

        if (bytes.Length == 0 || bytes.LongLength > (5 * 1024 * 1024 * 3) / 4)
        {
            return false;
        }

        attachment = new QueryPromptAttachment(
            QueryPromptAttachmentKind.Image,
            fullPath,
            name,
            DetectInlineImageMediaType(bytes),
            Convert.ToBase64String(bytes));
        return true;
    }

    private static string DetectInlineImageMediaType(byte[] buffer)
    {
        if (buffer.Length < 4)
        {
            return "image/png";
        }

        if (buffer[0] == 0x89 &&
            buffer[1] == 0x50 &&
            buffer[2] == 0x4E &&
            buffer[3] == 0x47)
        {
            return "image/png";
        }

        if (buffer[0] == 0xFF &&
            buffer[1] == 0xD8 &&
            buffer[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (buffer[0] == 0x47 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46)
        {
            return "image/gif";
        }

        if (buffer.Length >= 12 &&
            buffer[0] == 0x52 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46 &&
            buffer[3] == 0x46 &&
            buffer[8] == 0x57 &&
            buffer[9] == 0x45 &&
            buffer[10] == 0x42 &&
            buffer[11] == 0x50)
        {
            return "image/webp";
        }

        return "image/png";
    }

    private static readonly Regex PromptAttachmentMarkerPattern = new(
        @"<clawsharp-attachment>(?<json>.*?)</clawsharp-attachment>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static QueryRequestContentBlock? ToContentBlock(MessageContentBlock block)
    {
        return block.Kind switch
        {
            MessageContentKind.Text => new QueryRequestContentBlock("text", Text: block.Value),
            MessageContentKind.ToolUse => new QueryRequestContentBlock(
                "tool_use",
                Name: block.Name,
                ToolUseId: block.Metadata is not null && block.Metadata.TryGetValue("toolUseId", out var toolUseId)
                    ? toolUseId
                    : null,
                Input: block.Value),
            MessageContentKind.ToolResult => new QueryRequestContentBlock(
                "tool_result",
                Text: block.Value,
                Name: block.Name,
                ToolUseId: block.Metadata is not null && block.Metadata.TryGetValue("toolUseId", out var toolUseResultId)
                    ? toolUseResultId
                    : null,
                StructuredOutput: block.Metadata is not null && block.Metadata.TryGetValue("structuredOutput", out var structuredOutput)
                    ? structuredOutput
                    : null),
            _ => null
        };
    }
}
