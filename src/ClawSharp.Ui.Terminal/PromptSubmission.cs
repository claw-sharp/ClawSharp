// TS origin: ./components/PromptInput/PromptInput.tsx, ./components/PromptInput/inputModes.ts, ./hooks/useTextInput.ts
using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed record PromptSubmission(
    PromptInputMode Mode,
    string Value,
    string RawInput);
