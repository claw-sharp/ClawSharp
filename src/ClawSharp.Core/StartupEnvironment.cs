namespace ClawSharp.Core;

public sealed record StartupEnvironment(
    string ClaudeConfigHomeDir,
    bool BareMode = false,
    bool DisablePolicySkills = false,
    bool IsNonInteractive = false)
{
    public static StartupEnvironment Capture()
    {
        return new StartupEnvironment(
            SessionStoragePaths.GetClaudeConfigHomeDir(),
            IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_SIMPLE")),
            IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_POLICY_SKILLS")),
            IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_NON_INTERACTIVE")) || Console.IsInputRedirected);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}
