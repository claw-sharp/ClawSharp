namespace ClawSharp.Tools;

public sealed record ToolValidationResult(
    bool IsValid,
    string? ErrorMessage = null)
{
    public static ToolValidationResult Valid()
    {
        return new ToolValidationResult(true);
    }

    public static ToolValidationResult Invalid(string errorMessage)
    {
        return new ToolValidationResult(false, errorMessage);
    }
}
