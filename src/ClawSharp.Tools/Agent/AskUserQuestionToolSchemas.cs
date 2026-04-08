using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Agent;

internal static class AskUserQuestionToolSchemas
{
    public static readonly JsonObject QuestionOptionSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("label", ToolJsonSchemaFactory.String("The display text for this option that the user will see and select. Should be concise (1-5 words) and clearly describe the choice.")),
            ("description", ToolJsonSchemaFactory.String("Explanation of what this option means or what will happen if chosen. Useful for providing context about trade-offs or implications.")),
            ("preview", ToolJsonSchemaFactory.String("Optional preview content rendered when this option is focused. Use for mockups, code snippets, or visual comparisons that help users compare options. See the tool description for the expected content format."))
        },
        required: new[] { "label", "description" });

    public static readonly JsonObject QuestionSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("question", ToolJsonSchemaFactory.String("The complete question to ask the user. Should be clear, specific, and end with a question mark. Example: \"Which library should we use for date formatting?\" If multiSelect is true, phrase it accordingly, e.g. \"Which features do you want to enable?\"")),
            ("header", ToolJsonSchemaFactory.String("Very short label displayed as a chip/tag (max 12 chars). Examples: \"Auth method\", \"Library\", \"Approach\".")),
            ("options", ToolJsonSchemaFactory.Array(QuestionOptionSchema, minItems: 2, maxItems: 4, description: "The available choices for this question. Must have 2-4 options. Each option should be a distinct, mutually exclusive choice (unless multiSelect is enabled). There should be no 'Other' option, that will be provided automatically.")),
            ("multiSelect", ToolJsonSchemaFactory.Boolean(description: "Set to true to allow the user to select multiple options instead of just one. Use when choices are not mutually exclusive."))
        },
        required: new[] { "question", "header", "options" });

    public static readonly JsonObject AnnotationSchema = ToolJsonSchemaFactory.Object(
        new[]
        {
            ("preview", ToolJsonSchemaFactory.String("The preview content of the selected option, if the question used previews.")),
            ("notes", ToolJsonSchemaFactory.String("Free-text notes the user added to their selection."))
        });

    public static readonly JsonObject AnnotationsSchema = new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = AnnotationSchema.DeepClone(),
        ["description"] = "Optional per-question annotations from the user (e.g., notes on preview selections). Keyed by question text."
    };

    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("questions", (JsonNode)ToolJsonSchemaFactory.Array(QuestionSchema.DeepClone(), minItems: 1, maxItems: 4, description: "Questions to ask the user (1-4 questions)")),
            ("answers", (JsonNode)new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = ToolJsonSchemaFactory.String(),
                ["description"] = "User answers collected by the permission component"
            }),
            ("annotations", (JsonNode)AnnotationsSchema.DeepClone()),
            ("metadata", (JsonNode)ToolJsonSchemaFactory.Object(
                new[]
                {
                    ("source", (JsonNode)ToolJsonSchemaFactory.String("Optional identifier for the source of this question (e.g., \"remember\" for /remember command). Used for analytics tracking."))
                },
                description: "Optional metadata for tracking and analytics purposes. Not displayed to user."))
        },
        required: new[] { "questions" });

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("questions", (JsonNode)ToolJsonSchemaFactory.Array(QuestionSchema.DeepClone(), description: "The questions that were asked")),
            ("answers", (JsonNode)new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = ToolJsonSchemaFactory.String(),
                ["description"] = "The answers provided by the user (question text -> answer string; multi-select answers are comma-separated)"
            }),
            ("annotations", (JsonNode)AnnotationsSchema.DeepClone())
        },
        required: new[] { "questions", "answers" });
}
