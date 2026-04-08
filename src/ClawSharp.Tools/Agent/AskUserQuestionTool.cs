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
            if (!questionTexts.Add(q.Text))
            {
                return ToolValidationResult.Invalid("Question texts must be unique.");
            }

            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var opt in q.Options)
            {
                if (!labels.Add(opt.Label))
                {
                    return ToolValidationResult.Invalid($"Option labels must be unique within each question (duplicate label '{opt.Label}' in question '{q.Text}').");
                }
            }
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputNode = JsonNode.Parse(context.Arguments);
        var answersNode = inputNode?["answers"]?.AsObject();

        if (answersNode == null || answersNode.Count == 0)
        {
            return Failure(
                "AskUserQuestion requires collected user answers before it can succeed. No answers were attached to the tool input.");
        }

        return Success("User answered Claude's questions.", inputNode?.DeepClone());
    }

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        if (structuredOutput == null) return null;

        var answers = structuredOutput["answers"]?.AsObject();
        if (answers == null || answers.Count == 0) return null;
        var annotations = structuredOutput["annotations"]?.AsObject();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User answered Claude's questions:");
        foreach (var property in answers)
        {
            sb.AppendLine($"· {property.Key} → {property.Value?.GetValue<string>()}");

            if (annotations?[property.Key] is not JsonObject annotation)
            {
                continue;
            }

            var preview = annotation["preview"]?.GetValue<string>();
            var notes = annotation["notes"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(preview))
            {
                sb.AppendLine("  selected preview:");
                AppendIndentedBlock(sb, preview);
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                sb.AppendLine($"  user notes: {notes}");
            }
        }

        return sb.ToString();
    }

    private static bool TryParseArguments(string arguments, out List<AskUserQuestion>? questions, out string? errorMessage)
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

            questions = new List<AskUserQuestion>();
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

                questions.Add(new AskUserQuestion(questionText, header, options, multiSelect));
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void AppendIndentedBlock(System.Text.StringBuilder sb, string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            sb.Append("    ");
            sb.AppendLine(line);
        }
    }

    private record AskUserQuestion(string Text, string Header, List<QuestionOption> Options, bool MultiSelect);
    private record QuestionOption(string Label, string Description, string? Preview);
}
