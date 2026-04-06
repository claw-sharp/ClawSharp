// TS origin: ./components/PromptInput/inputModes.ts
namespace ClawSharp.Core;

public static class PromptInputModeHelpers
{
    public static string PrependModeCharacterToInput(string input, PromptInputMode mode)
    {
        return mode switch
        {
            PromptInputMode.Bash => $"!{input}",
            _ => input
        };
    }

    public static PromptInputMode GetModeFromInput(string input)
    {
        return input.StartsWith('!')
            ? PromptInputMode.Bash
            : PromptInputMode.Prompt;
    }

    public static string GetValueFromInput(string input)
    {
        var mode = GetModeFromInput(input);
        return mode == PromptInputMode.Prompt
            ? input
            : input[1..];
    }

    public static bool IsInputModeCharacter(string input)
    {
        return string.Equals(input, "!", StringComparison.Ordinal);
    }
}
