using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools.Skill;

internal sealed class SkillTool : BaseTool
{
    public SkillTool() : base(new ToolDescriptor(
        Name: "Skill",
        Description: "Load a discovered SKILL.md into the main conversation so its instructions are available for the current task.",
        Parameters:
        [
            new ToolParameter("skill", "The name of the skill to load. Slash-prefixed names like '/playwright' are also accepted."),
            new ToolParameter("args", "Optional arguments or extra focus for the skill.", Required: false)
        ],
        Aliases: ["skill"],
        InputSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["skill"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The name of the skill to invoke."
                },
                ["args"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional arguments or additional focus for the skill."
                }
            },
            ["required"] = new JsonArray { "skill" },
            ["additionalProperties"] = false
        },
        OutputSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["commandName"] = new JsonObject { ["type"] = "string" },
                ["status"] = new JsonObject { ["type"] = "string" },
                ["resolvedSkill"] = new JsonObject { ["type"] = "string" },
                ["source"] = new JsonObject { ["type"] = "string" },
                ["filePath"] = new JsonObject { ["type"] = "string" },
                ["baseDirectory"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray { "success", "commandName", "status", "resolvedSkill", "source", "filePath", "baseDirectory" },
            ["additionalProperties"] = false
        },
        Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;

    public override bool IsReadOnly(string arguments) => true;

    public override string? RenderToolUseMessage(string arguments)
    {
        try
        {
            var input = JsonSerializer.Deserialize<SkillInput>(arguments);
            return string.IsNullOrWhiteSpace(input?.skill)
                ? "Loading skill"
                : $"Loading skill {NormalizeRequestedSkillName(input.skill)}";
        }
        catch
        {
            return "Loading skill";
        }
    }

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        return content;
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        SkillInput? input;
        try
        {
            input = JsonSerializer.Deserialize<SkillInput>(context.Arguments);
        }
        catch (JsonException)
        {
            return Failure("Invalid input.");
        }

        if (input is null)
        {
            return Failure("Invalid input.");
        }

        if (string.IsNullOrWhiteSpace(input.skill))
        {
            return Failure("Skill name is required.");
        }

        var requestedSkillName = NormalizeRequestedSkillName(input.skill);
        var skill = ResolveSkill(context.AppState.Skills, requestedSkillName);
        if (skill is null)
        {
            return Failure(BuildUnknownSkillMessage(requestedSkillName, context.AppState.Skills));
        }

        if (!File.Exists(skill.FilePath))
        {
            return Failure($"Skill \"{skill.Name}\" was discovered, but its file no longer exists: {skill.FilePath}");
        }

        string rawSkillText;
        try
        {
            rawSkillText = await File.ReadAllTextAsync(skill.FilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure($"Failed to read skill \"{skill.Name}\": {ex.Message}");
        }

        var skillBody = StripFrontmatter(rawSkillText).Trim();
        if (string.IsNullOrWhiteSpace(skillBody))
        {
            return Failure($"Skill \"{skill.Name}\" did not contain any usable instructions.");
        }

        var injectedPrompt = BuildInjectedPrompt(skill, skillBody, input.args);
        var injectedMessage = ChatMessageFactory.CreateUserMessage(injectedPrompt, isMeta: true);

        return Success(
            $"Skill \"{skill.Name}\" loaded from {skill.Source}. Follow the injected instructions.",
            new JsonObject
            {
                ["success"] = true,
                ["commandName"] = requestedSkillName,
                ["status"] = "inline",
                ["resolvedSkill"] = skill.Name,
                ["source"] = skill.Source,
                ["filePath"] = Path.GetFullPath(skill.FilePath),
                ["baseDirectory"] = NormalizeDirectoryPath(skill.BaseDirectory)
            },
            [injectedMessage]);
    }

    private static string BuildInjectedPrompt(DiscoveredSkill skill, string skillBody, string? args)
    {
        var builder = new StringBuilder();
        builder.Append("Base directory for this skill: ");
        builder.AppendLine(NormalizeDirectoryPath(skill.BaseDirectory));
        builder.AppendLine();
        builder.Append(skillBody);

        if (!string.IsNullOrWhiteSpace(args))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("## Additional Focus / Arguments");
            builder.AppendLine();
            builder.Append(args.Trim());
        }

        return builder.ToString();
    }

    private static string BuildUnknownSkillMessage(string requestedSkillName, IReadOnlyList<DiscoveredSkill> skills)
    {
        if (skills.Count == 0)
        {
            return $"Unknown skill: {requestedSkillName}. No skills are currently loaded.";
        }

        var suggestions = string.Join(", ", skills
            .Select(static skill => skill.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Take(12));

        return $"Unknown skill: {requestedSkillName}. Available skills: {suggestions}";
    }

    private static DiscoveredSkill? ResolveSkill(IReadOnlyList<DiscoveredSkill> skills, string requestedSkillName)
    {
        var exact = skills.FirstOrDefault(skill =>
            string.Equals(skill.Name, requestedSkillName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var suffixMatchName = requestedSkillName.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(suffixMatchName))
        {
            return null;
        }

        var matches = skills
            .Where(skill => string.Equals(skill.Name, suffixMatchName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string NormalizeRequestedSkillName(string rawSkillName)
    {
        return rawSkillName.Trim().TrimStart('/');
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private static string StripFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return text;
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return text;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                return reader.ReadToEnd();
            }
        }

        return text;
    }

    private sealed class SkillInput
    {
        public string skill { get; set; } = string.Empty;
        public string? args { get; set; }
    }
}
