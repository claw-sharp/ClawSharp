// TS origin: ./Tool.ts, ./types/tools.ts, ./tools/TaskOutputTool/TaskOutputTool.tsx
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public sealed record ToolProgressUpdate(
    string ToolUseId,
    JsonObject Data);
