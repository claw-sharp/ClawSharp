namespace ClawSharp.Core;

public static class MainLoopModelResolver
{
    public const string PlaceholderModel = "foundation-placeholder";
    public const string DefaultMainLoopModel = ProviderRuntimeResolver.DefaultAnthropicModel;

    public static string Resolve(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return ProviderRuntimeResolver.GetDefaultModelForCurrentProvider();
        }

        var trimmed = model.Trim();
        return string.Equals(trimmed, PlaceholderModel, StringComparison.OrdinalIgnoreCase)
            ? ProviderRuntimeResolver.GetDefaultModelForCurrentProvider()
            : trimmed;
    }

    public static string Resolve(string? preferredModel, string? fallbackModel)
    {
        if (!string.IsNullOrWhiteSpace(preferredModel) &&
            !string.Equals(preferredModel.Trim(), PlaceholderModel, StringComparison.OrdinalIgnoreCase))
        {
            return preferredModel.Trim();
        }

        return Resolve(fallbackModel);
    }

    public static string RenderSetting(string? model)
    {
        var resolved = Resolve(model);
        return resolved switch
        {
            "sonnet" => "Sonnet",
            "opus" => "Opus",
            "haiku" => "Haiku",
            "gpt-4o" => "GPT-4o",
            "gpt-4o-mini" => "GPT-4o Mini",
            "gpt-4.1" => "GPT-4.1",
            "openai/gpt-4.1" => "GitHub GPT-4.1",
            "gpt-5.4" => "GPT-5.4",
            "gpt-5.4-mini" => "GPT-5.4 Mini",
            "gpt-5.3-codex" => "GPT-5.3 Codex",
            "gpt-5.3-codex-spark" => "GPT-5.3 Codex Spark",
            "gpt-5.2-codex" => "GPT-5.2 Codex",
            "gpt-5.1-codex-max" => "GPT-5.1 Codex Max",
            "gpt-5.1-codex-mini" => "GPT-5.1 Codex Mini",
            "gemini-2.0-flash" => "Gemini 2.0 Flash",
            _ when resolved.StartsWith("claude-sonnet-4-6", StringComparison.OrdinalIgnoreCase) => "Sonnet 4.6",
            _ when resolved.StartsWith("claude-opus-4-6", StringComparison.OrdinalIgnoreCase) => "Opus 4.6",
            _ when resolved.StartsWith("claude-sonnet-4-5", StringComparison.OrdinalIgnoreCase) => "Sonnet 4.5",
            _ when resolved.StartsWith("claude-opus-4-5", StringComparison.OrdinalIgnoreCase) => "Opus 4.5",
            _ when resolved.StartsWith("claude-opus-4-1", StringComparison.OrdinalIgnoreCase) => "Opus 4.1",
            _ when resolved.StartsWith("claude-opus-4-", StringComparison.OrdinalIgnoreCase) => "Opus 4",
            _ when resolved.StartsWith("claude-sonnet-4-", StringComparison.OrdinalIgnoreCase) => "Sonnet 4",
            _ when resolved.StartsWith("claude-haiku-4-5", StringComparison.OrdinalIgnoreCase) => "Haiku 4.5",
            _ when resolved.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) => resolved.Replace('-', ' '),
            _ when resolved.StartsWith("claude-3-7-sonnet", StringComparison.OrdinalIgnoreCase) => "Sonnet 3.7",
            _ when resolved.StartsWith("claude-3-5-sonnet", StringComparison.OrdinalIgnoreCase) => "Sonnet 3.5",
            _ when resolved.StartsWith("claude-3-5-haiku", StringComparison.OrdinalIgnoreCase) => "Haiku 3.5",
            _ when resolved.StartsWith("claude-3-opus", StringComparison.OrdinalIgnoreCase) => "Opus 3",
            _ when resolved.StartsWith("claude-3-sonnet", StringComparison.OrdinalIgnoreCase) => "Sonnet 3",
            _ when resolved.StartsWith("claude-3-haiku", StringComparison.OrdinalIgnoreCase) => "Haiku 3",
            _ => resolved
        };
    }
}
