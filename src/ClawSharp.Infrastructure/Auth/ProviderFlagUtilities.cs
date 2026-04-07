using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class ProviderFlagUtilities
{
    private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "anthropic",
        "openai",
        "gemini",
        "github",
        "bedrock",
        "vertex",
        "foundry",
        "ollama",
        "codex"
    };

    public static string? ParseProviderFlag(IReadOnlyList<string> args)
    {
        return TryGetOptionValue(args, "--provider", out var value) ? value : null;
    }

    public static string? ParseModelFlag(IReadOnlyList<string> args)
    {
        return TryGetOptionValue(args, "--model", out var value) ? value : null;
    }

    public static IReadOnlyList<string> StripProviderFlags(IReadOnlyList<string> args)
    {
        var stripped = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--provider":
                case "--model":
                    index++;
                    break;
                default:
                    if (args[index].StartsWith("--provider=", StringComparison.Ordinal) ||
                        args[index].StartsWith("--model=", StringComparison.Ordinal))
                    {
                        break;
                    }

                    stripped.Add(args[index]);
                    break;
            }
        }

        return stripped;
    }

    public static string? ApplyProviderFlags(IReadOnlyList<string> args)
    {
        var provider = ParseProviderFlag(args);
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        if (!ValidProviders.Contains(provider))
        {
            return $"Unknown provider \"{provider}\". Valid providers: {string.Join(", ", ValidProviders.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))}";
        }

        ClearProviderFlags();
        var model = ParseModelFlag(args);

        switch (provider.Trim().ToLowerInvariant())
        {
            case "anthropic":
                if (!string.IsNullOrWhiteSpace(model))
                {
                    Environment.SetEnvironmentVariable("ANTHROPIC_MODEL", model.Trim());
                }
                break;
            case "openai":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", "1");
                if (!string.IsNullOrWhiteSpace(model))
                {
                    Environment.SetEnvironmentVariable("OPENAI_MODEL", model.Trim());
                }
                break;
            case "gemini":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_GEMINI", "1");
                if (!string.IsNullOrWhiteSpace(model))
                {
                    Environment.SetEnvironmentVariable("GEMINI_MODEL", model.Trim());
                }
                break;
            case "github":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_GITHUB", "1");
                if (!string.IsNullOrWhiteSpace(model))
                {
                    Environment.SetEnvironmentVariable("OPENAI_MODEL", model.Trim());
                }
                break;
            case "bedrock":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK", "1");
                break;
            case "vertex":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX", "1");
                break;
            case "foundry":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY", "1");
                break;
            case "ollama":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", "1");
                Environment.SetEnvironmentVariable("OPENAI_BASE_URL", "http://localhost:11434/v1");
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "ollama");
                Environment.SetEnvironmentVariable("OPENAI_MODEL", string.IsNullOrWhiteSpace(model) ? "llama3.2" : model.Trim());
                break;
            case "codex":
                Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", "1");
                Environment.SetEnvironmentVariable("OPENAI_BASE_URL", ProviderRuntimeResolver.DefaultCodexBaseUrl);
                Environment.SetEnvironmentVariable("OPENAI_MODEL", string.IsNullOrWhiteSpace(model) ? ProviderRuntimeResolver.DefaultCodexModel : model.Trim());
                break;
        }

        return null;
    }

    private static bool TryGetOptionValue(IReadOnlyList<string> args, string optionName, out string value)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[index + 1];
                    return true;
                }

                break;
            }

            if (args[index].StartsWith(optionName + "=", StringComparison.Ordinal))
            {
                value = args[index][(optionName.Length + 1)..];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void ClearProviderFlags()
    {
        foreach (var variableName in new[]
                 {
                     "CLAUDE_CODE_USE_OPENAI",
                     "CLAUDE_CODE_USE_GEMINI",
                     "CLAUDE_CODE_USE_GITHUB",
                     "CLAUDE_CODE_USE_BEDROCK",
                     "CLAUDE_CODE_USE_VERTEX",
                     "CLAUDE_CODE_USE_FOUNDRY"
                 })
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }
}
