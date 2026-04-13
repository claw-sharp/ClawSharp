using System.Text;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Query.Skills;

internal sealed class PromptSkillReferenceService
{
    private static readonly Regex SkillReferencePattern = new(
        @"(?<![\w:])\$(?<name>[A-Za-z0-9][A-Za-z0-9:_-]*)",
        RegexOptions.Compiled);

    public async Task<QueryTurnRequest> ExpandPromptSkillReferencesAsync(
        QueryTurnRequest request,
        IReadOnlyList<DiscoveredSkill> discoveredSkills,
        CancellationToken cancellationToken = default)
    {
        if (discoveredSkills.Count == 0 || string.IsNullOrWhiteSpace(request.EffectiveUserInput))
        {
            return request;
        }

        var referencedSkillNames = SkillReferencePattern.Matches(request.EffectiveUserInput)
            .Select(static match => match.Groups["name"].Value)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (referencedSkillNames.Length == 0)
        {
            return request;
        }

        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var userContext = new Dictionary<string, string>(modelTurnContext.UserContext, StringComparer.Ordinal);
        var addedSkillCount = 0;

        foreach (var referencedSkillName in referencedSkillNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skill = ResolveSkill(discoveredSkills, referencedSkillName);
            if (skill is null || !File.Exists(skill.FilePath))
            {
                continue;
            }

            string rawSkillText;
            try
            {
                rawSkillText = await File.ReadAllTextAsync(skill.FilePath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var skillBody = StripFrontmatter(rawSkillText).Trim();
            if (string.IsNullOrWhiteSpace(skillBody))
            {
                continue;
            }

            userContext[$"Skill ${skill.Name}"] = BuildInjectedPrompt(skill, skillBody);
            addedSkillCount += 1;
        }

        if (addedSkillCount == 0)
        {
            return request;
        }

        return request with
        {
            ModelTurnContext = new QueryModelTurnContext(
                modelTurnContext.SystemPrompt,
                userContext,
                modelTurnContext.SystemContext,
                modelTurnContext.QuerySource)
        };
    }

    private static DiscoveredSkill? ResolveSkill(
        IReadOnlyList<DiscoveredSkill> skills,
        string requestedSkillName)
    {
        var exact = skills.FirstOrDefault(skill =>
            string.Equals(skill.Name, requestedSkillName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = skills
            .Where(skill => string.Equals(GetSuffixName(skill.Name), requestedSkillName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string GetSuffixName(string skillName)
    {
        return skillName.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? skillName;
    }

    private static string BuildInjectedPrompt(DiscoveredSkill skill, string skillBody)
    {
        var builder = new StringBuilder();
        builder.Append("Base directory for this skill: ");
        builder.AppendLine(NormalizeDirectoryPath(skill.BaseDirectory));
        builder.AppendLine();
        builder.Append(skillBody);
        return builder.ToString();
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
}
