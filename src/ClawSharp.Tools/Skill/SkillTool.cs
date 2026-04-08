using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools.Skill;

internal sealed class SkillTool : BaseTool
{
    private readonly SkillRegistry _registry;

    public SkillTool(SkillRegistry registry) : base(new ToolDescriptor(
        Name: "skill",
        Description: "Invoke a named 'skill' which expands into a full set of instructions or launches a specialized agent. Use this for complex tasks like 'simplify', 'review-pr', etc.",
        InputSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["skill"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The name of the skill to invoke (e.g., 'simplify')"
                },
                ["args"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional arguments for the skill"
                }
            },
            ["required"] = new JsonArray { "skill" }
        }))
    {
        _registry = registry;
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputJson = context.Arguments;
        var input = JsonSerializer.Deserialize<SkillInput>(inputJson);
        if (input == null) return Failure("Invalid input");

        if (!_registry.TryResolve(input.skill, out var skill) || skill == null)
        {
            return Failure($"Unknown skill: {input.skill}");
        }

        if (skill.Context == "inline")
        {
            // Inject the prompt as a user message
            var prompt = skill.Prompt;
            if (!string.IsNullOrEmpty(input.args))
            {
                prompt += $"\n\n## Additional Focus / Arguments\n\n{input.args}";
            }

            var injectedMessage = new ChatMessage(
                Id: Guid.NewGuid().ToString(),
                Role: MessageRole.User,
                ContentBlocks: new List<MessageContentBlock> { 
                    new MessageContentBlock(MessageContentKind.Text, prompt) 
                },
                Timestamp: DateTimeOffset.UtcNow
            );

            return Success($"Skill \"{skill.Name}\" loaded and active. Follow the newly injected instructions.", 
                new JsonObject { ["status"] = "inline", ["skill"] = skill.Name },
                new List<ChatMessage> { injectedMessage });
        }
        else if (skill.Context == "fork")
        {
            // TODO: Implement forked agent execution once infrastructure is ready
            return Failure("Forked execution not yet implemented in SkillTool.");
        }

        return Failure($"Unsupported skill context: {skill.Context}");
    }

    private class SkillInput
    {
        public string skill { get; set; } = string.Empty;
        public string? args { get; set; }
    }
}
