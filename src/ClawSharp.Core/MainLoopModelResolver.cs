// TS origin: ./hooks/useMainLoopModel.ts, ./utils/model/model.ts
namespace ClawSharp.Core;

public static class MainLoopModelResolver
{
    public const string PlaceholderModel = "foundation-placeholder";

    // public const string DefaultMainLoopModel = "claude-sonnet-4-6";
    public const string DefaultMainLoopModel = "claude-haiku-4-5-20251001";

    public static string Resolve(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return DefaultMainLoopModel;
        }

        var trimmed = model.Trim();
        return string.Equals(trimmed, PlaceholderModel, StringComparison.OrdinalIgnoreCase)
            ? DefaultMainLoopModel
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
            _ when resolved.StartsWith("claude-sonnet-4-6", StringComparison.OrdinalIgnoreCase) => "Sonnet 4.6",
            _ when resolved.StartsWith("claude-opus-4-6", StringComparison.OrdinalIgnoreCase) => "Opus 4.6",
            _ when resolved.StartsWith("claude-sonnet-4-5", StringComparison.OrdinalIgnoreCase) => "Sonnet 4.5",
            _ when resolved.StartsWith("claude-opus-4-5", StringComparison.OrdinalIgnoreCase) => "Opus 4.5",
            _ when resolved.StartsWith("claude-opus-4-1", StringComparison.OrdinalIgnoreCase) => "Opus 4.1",
            _ when resolved.StartsWith("claude-opus-4-", StringComparison.OrdinalIgnoreCase) => "Opus 4",
            _ when resolved.StartsWith("claude-sonnet-4-", StringComparison.OrdinalIgnoreCase) => "Sonnet 4",
            _ when resolved.StartsWith("claude-haiku-4-5", StringComparison.OrdinalIgnoreCase) => "Haiku 4.5",
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
