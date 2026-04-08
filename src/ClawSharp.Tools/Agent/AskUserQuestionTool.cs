using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools.Agent;

internal sealed class AskUserQuestionTool : BaseTool
{
    public const string ToolName = "AskUserQuestion";

    public AskUserQuestionTool()
        : base(new ToolDescriptor(
            ToolName,
            "Asks the user multiple choice questions to gather information, clarify ambiguity, understand preferences, make decisions or offer them choices.",
            SearchHint: "prompt the user with a multiple-choice question",
            ShouldDefer: true,
            InputSchema: AskUserQuestionToolSchemas.InputSchema,
            OutputSchema: AskUserQuestionToolSchemas.OutputSchema,
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override async Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var questions, out var errorMessage) || questions == null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Invalid input.");
        }

        // Uniqueness check
        var questionTexts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in questions)
        {
            if (!questionTexts.Add(q.Question))
            {
                return ToolValidationResult.Invalid("Question texts must be unique.");
            }

            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var opt in q.Options)
            {
                if (!labels.Add(opt.Label))
                {
                    return ToolValidationResult.Invalid($"Option labels must be unique within each question (duplicate label '{opt.Label}' in question '{q.Question}').");
                }

                // HTML preview check
                if (!string.IsNullOrWhiteSpace(opt.Preview))
                {
                    var htmlErr = ValidateHtmlPreview(opt.Preview);
                    if (htmlErr != null)
                    {
                        return ToolValidationResult.Invalid($"Option '{opt.Label}' in question '{q.Question}': {htmlErr}");
                    }
                }
            }
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputNode = JsonNode.Parse(context.Arguments);
        var answersNode = inputNode?["answers"]?.AsObject();
        var annotationsNode = inputNode?["annotations"]?.AsObject();

        if (answersNode == null || answersNode.Count == 0)
        {
            // If we are here and answers are missing, it means the interaction didn't happen or didn't inject answers.
            // In a fully ported system, the prompter would have handled this.
            // For now, we'll return a message indicating that the user hasn't answered yet, 
            // or we could try to prompt the user if the environment allows it.
            
            // However, looking at parity, the tool is 'deferred'. 
            // If the orchestrator doesn't handle the deferred interaction, we might want to fail or wait.
            
            // For now, let's return a result that mimics the 'data' structure in TS.
            return Success("Questions asked.", inputNode?.DeepClone());
        }

        return Success("User answered Claude's questions.", inputNode?.DeepClone());
    }

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        if (structuredOutput == null) return null;

        var answers = structuredOutput["answers"]?.AsObject();
        if (answers == null || answers.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User answered Claude's questions:");
        foreach (var property in answers)
        {
            sb.AppendLine($"· {property.Key} → {property.Value?.GetValue<string>()}");
        }

        return sb.ToString();
    }

    private static bool TryParseArguments(string arguments, out List<Question>? questions, out string? errorMessage)
    {
        questions = null;
        errorMessage = null;

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (!doc.RootElement.TryGetProperty("questions", out var questionsElement) || questionsElement.ValueKind != JsonValueKind.Array)
            {
                errorMessage = "Missing or invalid 'questions' array.";
                return false;
            }

            questions = new List<Question>();
            foreach (var qEl in questionsElement.EnumerateArray())
            {
                var questionText = qEl.GetProperty("question").GetString() ?? "";
                var header = qEl.GetProperty("header").GetString() ?? "";
                var multiSelect = qEl.TryGetProperty("multiSelect", out var msEl) && msEl.GetBoolean();
                
                var options = new List<QuestionOption>();
                foreach (var optEl in qEl.GetProperty("options").EnumerateArray())
                {
                    options.Add(new QuestionOption(
                        optEl.GetProperty("label").GetString() ?? "",
                        optEl.GetProperty("description").GetString() ?? "",
                        optEl.TryGetProperty("preview", out var pEl) ? pEl.GetString() : null
                    ));
                }

                questions.Add(new Question(questionText, header, options, multiSelect));
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string? ValidateHtmlPreview(string preview)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(preview, @"<\s*(html|body|!doctype)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "preview must be an HTML fragment, not a full document (no <html>, <body>, or <!DOCTYPE>)";
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(preview, @"<\s*(script|style)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "preview must not contain <script> or <style> tags. Use inline styles via the style attribute if needed.";
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(preview, @"<[a-z][^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "preview must contain HTML (previewFormat is set to \"html\"). Wrap content in a tag like <div> or <pre>.";
        }
        return null;
    }

    private record Question(string Question, string Header, List<QuestionOption> Options, bool MultiSelect);
    private record QuestionOption(string Label, string Description, string? Preview);
}
