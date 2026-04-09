using System.Collections.Concurrent;

namespace ClawSharp.Tools.Skill;

public sealed class SkillRegistry
{
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);

    public SkillRegistry()
    {
        // Register some bundled skills for testing
        Register(new SkillDefinition(
            Name: "simplify",
            Description: "Review changed code for reuse, quality, and efficiency, then fix any issues found.",
            Prompt: "# Simplify: Code Review and Cleanup\n\nReview all changed files for reuse, quality, and efficiency. Fix any issues found.\n\n## Phase 1: Identify Changes\n\nRun `git diff` to see what changed.",
            UserInvocable: true,
            Context: "inline"
        ));
    }

    public void Register(SkillDefinition skill)
    {
        _skills[skill.Name] = skill;
    }

    public bool TryResolve(string name, out SkillDefinition? skill)
    {
        return _skills.TryGetValue(name, out skill);
    }

    public IReadOnlyList<SkillDefinition> GetAll()
    {
        return _skills.Values.ToList();
    }
}
