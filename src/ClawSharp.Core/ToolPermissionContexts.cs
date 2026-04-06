namespace ClawSharp.Core;

public static class ToolPermissionContexts
{
    public static ToolPermissionContext CreateEmpty(PermissionMode mode = PermissionMode.Default)
    {
        return new ToolPermissionContext(
            mode,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            CreateEmptyRules(),
            CreateEmptyRules(),
            CreateEmptyRules(),
            IsBypassPermissionsModeAvailable: false);
    }

    private static IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> CreateEmptyRules()
    {
        return new Dictionary<PermissionRuleSource, IReadOnlyList<string>>();
    }
}
