using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed record PromptSubmission(
    PromptInputMode Mode,
    string Value,
    string RawInput);
