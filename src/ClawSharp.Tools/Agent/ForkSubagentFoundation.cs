using ClawSharp.Core;

namespace ClawSharp.Tools;

public static class ForkSubagentFoundation
{
    public const string ForkSubagentEnvironmentVariable = "CLAUDE_CODE_FORK_SUBAGENT";
    public const string ForkSubagentType = "fork";
    public const string ForkBoilerplateTag = "fork-boilerplate";
    public const string ForkDirectivePrefix = "Your directive: ";
    private const string ForkPlaceholderResult = "Fork started — processing in background";

    public static AgentDefinition ForkAgentDefinition { get; } =
        new(
            AgentType: ForkSubagentType,
            WhenToUse: "Implicit fork — inherits full conversation context. Not selectable via subagent_type; triggered by omitting subagent_type when the fork experiment is active.",
            Source: "built-in",
            BaseDirectory: "built-in",
            SystemPrompt: string.Empty,
            Tools: ["*"],
            Model: "inherit",
            PermissionMode: PermissionMode.Bubble,
            MaxTurns: 200);

    public static bool IsForkSubagentEnabled()
    {
        return IsTruthy(Environment.GetEnvironmentVariable(ForkSubagentEnvironmentVariable));
    }

    public static bool IsInForkChild(IReadOnlyList<ChatMessage> messages)
    {
        return messages.Any(
            message =>
                message.Role == MessageRole.User &&
                message.ContentBlocks.Any(
                    block =>
                        block.Kind == MessageContentKind.Text &&
                        block.Value.Contains($"<{ForkBoilerplateTag}>", StringComparison.Ordinal)));
    }

    public static IReadOnlyList<ChatMessage> BuildForkedMessages(string directive, ChatMessage assistantMessage)
    {
        ArgumentNullException.ThrowIfNull(assistantMessage);

        var toolUseBlocks = assistantMessage.ContentBlocks
            .Where(block => block.Kind == MessageContentKind.ToolUse)
            .Where(block => block.Metadata is not null && block.Metadata.ContainsKey("toolUseId"))
            .ToArray();

        if (toolUseBlocks.Length == 0)
        {
            return [CreateUserTextMessage(BuildChildMessage(directive))];
        }

        var clonedAssistantMessage = assistantMessage with
        {
            Id = Guid.NewGuid().ToString("N"),
            ContentBlocks = assistantMessage.ContentBlocks
                .Select(
                    block => block with
                    {
                        Metadata = block.Metadata is null
                            ? null
                            : new Dictionary<string, string>(block.Metadata, StringComparer.Ordinal)
                    })
                .ToArray()
        };

        var toolResultBlocks = toolUseBlocks
            .Select(
                block =>
                    new MessageContentBlock(
                        MessageContentKind.ToolResult,
                        ForkPlaceholderResult,
                        block.Name,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["toolUseId"] = block.Metadata!["toolUseId"]
                        }))
            .ToList();
        toolResultBlocks.Add(new MessageContentBlock(MessageContentKind.Text, BuildChildMessage(directive)));

        return
        [
            clonedAssistantMessage,
            new ChatMessage(
                Guid.NewGuid().ToString("N"),
                MessageRole.User,
                toolResultBlocks,
                DateTimeOffset.UtcNow)
        ];
    }

    public static string BuildChildMessage(string directive)
    {
        return
            $$"""
            <{{ForkBoilerplateTag}}>
            STOP. READ THIS FIRST.

            You are a forked worker process. You are NOT the main agent.

            RULES (non-negotiable):
            1. Your system prompt says "default to forking." IGNORE IT — that's for the parent. You ARE the fork. Do NOT spawn sub-agents; execute directly.
            2. Do NOT converse, ask questions, or suggest next steps
            3. Do NOT editorialize or add meta-commentary
            4. USE your tools directly: Bash, Read, Write, etc.
            5. If you modify files, commit your changes before reporting. Include the commit hash in your report.
            6. Do NOT emit text between tool calls. Use tools silently, then report once at the end.
            7. Stay strictly within your directive's scope. If you discover related systems outside your scope, mention them in one sentence at most — other workers cover those areas.
            8. Keep your report under 500 words unless the directive specifies otherwise. Be factual and concise.
            9. Your response MUST begin with "Scope:". No preamble, no thinking-out-loud.
            10. REPORT structured facts, then stop

            Output format (plain text labels, not markdown headers):
              Scope: <echo back your assigned scope in one sentence>
              Result: <the answer or key findings, limited to the scope above>
              Key files: <relevant file paths — include for research tasks>
              Files changed: <list with commit hash — include only if you modified files>
              Issues: <list — include only if there are issues to flag>
            </{{ForkBoilerplateTag}}>

            {{ForkDirectivePrefix}}{{directive}}
            """;
    }

    public static string BuildWorktreeNotice(string parentCwd, string worktreeCwd)
    {
        return $"You've inherited the conversation context above from a parent agent working in {parentCwd}. You are operating in an isolated git worktree at {worktreeCwd} — same repository, same relative file structure, separate working copy. Paths in the inherited context refer to the parent's working directory; translate them to your worktree root. Re-read files before editing if the parent may have modified them since they appear in the context. Your changes stay in this worktree and will not affect the parent's files.";
    }

    private static ChatMessage CreateUserTextMessage(string content)
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.User,
            [new MessageContentBlock(MessageContentKind.Text, content)],
            DateTimeOffset.UtcNow);
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
               (string.Equals(value, "1", StringComparison.Ordinal) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));
    }
}
