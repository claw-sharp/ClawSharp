# Tool Conversion Implementation Plan

This plan outlines the conversion of tools from the TypeScript-based `claude-code` repository to the .NET-based `ClawSharp` repository.

## Current Status

| Tool Name | Source Path (TypeScript) | Target Status (ClawSharp) | Target Namespace / Path |
| :--- | :--- | :--- | :--- |
| **Agent** | | | |
| AgentTool | `tools/AgentTool` | ✅ Existing | `ClawSharp.Tools.AgentTool` |
| SendMessageTool | `tools/SendMessageTool` | ✅ Existing | `ClawSharp.Tools.SendMessageTool` |
| TaskOutputTool | `tools/TaskOutputTool` | ✅ Existing | `ClawSharp.Tools.TaskOutputTool` |
| TaskStopTool | `tools/TaskStopTool` | ✅ Existing | `ClawSharp.Tools.TaskStopTool` |
| **File** | | | |
| FileReadTool | `tools/FileReadTool` | ✅ Existing | `ClawSharp.Tools.ReadTool` |
| FileWriteTool | `tools/FileWriteTool` | ✅ Existing | `ClawSharp.Tools.WriteTool` |
| FileEditTool | `tools/FileEditTool` | ✅ Existing | `ClawSharp.Tools.EditTool` |
| GlobTool | `tools/GlobTool` | ✅ Existing | `ClawSharp.Tools.GlobTool` |
| NotebookEditTool | `tools/NotebookEditTool` | ❌ Missing | `ClawSharp.Tools.File.NotebookEditTool` |
| **Shell** | | | |
| BashTool | `tools/BashTool` | ✅ Existing | `ClawSharp.Tools.BashTool` |
| PowerShellTool | `tools/PowerShellTool` | ✅ Existing | `ClawSharp.Tools.PowerShellTool` |
| **Search** | | | |
| GrepTool | `tools/GrepTool` | ✅ Existing | `ClawSharp.Tools.GrepTool` |
| **MCP** | | | |
| ListMcpResourcesTool | `tools/ListMcpResourcesTool` | ❌ Missing | `ClawSharp.Tools.Mcp.ListResourcesTool` |
| ReadMcpResourceTool | `tools/ReadMcpResourceTool` | ❌ Missing | `ClawSharp.Tools.Mcp.ReadResourceTool` |
| MCPTool (Generic) | `tools/MCPTool` | ❌ Missing | `ClawSharp.Tools.Mcp.McpTool` |
| McpAuthTool | `tools/McpAuthTool` | ❌ Missing | `ClawSharp.Tools.Mcp.McpAuthTool` |
| **Tasks / TODO** | | | |
| TaskCreateTool | `tools/TaskCreateTool` | ❌ Missing | `ClawSharp.Tools.Tasks.CreateTool` |
| TaskGetTool | `tools/TaskGetTool` | ❌ Missing | `ClawSharp.Tools.Tasks.GetTool` |
| TaskListTool | `tools/TaskListTool` | ❌ Missing | `ClawSharp.Tools.Tasks.ListTool` |
| TaskUpdateTool | `tools/TaskUpdateTool` | ❌ Missing | `ClawSharp.Tools.Tasks.UpdateTool` |
| TodoWriteTool | `tools/TodoWriteTool` | ❌ Missing | `ClawSharp.Tools.Tasks.TodoWriteTool` |
| **Worktree / Plan** | | | |
| EnterPlanModeTool | `tools/EnterPlanModeTool` | ❌ Missing | `ClawSharp.Tools.Plan.EnterPlanModeTool` |
| ExitPlanModeTool | `tools/ExitPlanModeTool` | ❌ Missing | `ClawSharp.Tools.Plan.ExitPlanModeTool` |
| EnterWorktreeTool | `tools/EnterWorktreeTool` | ❌ Missing | `ClawSharp.Tools.Worktree.EnterWorktreeTool` |
| ExitWorktreeTool | `tools/ExitWorktreeTool` | ❌ Missing | `ClawSharp.Tools.Worktree.ExitWorktreeTool` |
| VerifyPlanExecutionTool | `tools/VerifyPlanExecutionTool` | ❌ Missing | `ClawSharp.Tools.Plan.VerifyPlanExecutionTool` |
| **Web** | | | |
| WebFetchTool | `tools/WebFetchTool` | ❌ Missing | `ClawSharp.Tools.Web.FetchTool` |
| WebSearchTool | `tools/WebSearchTool` | ❌ Missing | `ClawSharp.Tools.Web.SearchTool` |
| WebBrowserTool | `tools/WebBrowserTool` | ❌ Missing | `ClawSharp.Tools.Web.BrowserTool` |
| **Other** | | | |
| AskUserQuestionTool | `tools/AskUserQuestionTool` | ❌ Missing | `ClawSharp.Tools.Agent.AskUserQuestionTool` |
| BriefTool | `tools/BriefTool` | ❌ Missing | `ClawSharp.Tools.Agent.BriefTool` |
| ConfigTool | `tools/ConfigTool` | ❌ Missing | `ClawSharp.Tools.ConfigTool` |
| LSPTool | `tools/LSPTool` | ❌ Missing | `ClawSharp.Tools.Lsp.LspTool` |
| REPLTool | `tools/REPLTool` | ❌ Missing | `ClawSharp.Tools.Repl.ReplTool` |
| RemoteTriggerTool | `tools/RemoteTriggerTool` | ❌ Missing | `ClawSharp.Tools.Agent.RemoteTriggerTool` |
| ScheduleCronTool | `tools/ScheduleCronTool` | ❌ Missing | `ClawSharp.Tools.Agent.CronTool` |
| SkillTool | `tools/SkillTool` | ❌ Missing | `ClawSharp.Tools.SkillTool` |
| SleepTool | `tools/SleepTool" | ❌ Missing | `ClawSharp.Tools.Agent.SleepTool` |
| SyntheticOutputTool | `tools/SyntheticOutputTool` | ❌ Missing | `ClawSharp.Tools.Agent.SyntheticOutputTool` |
| TeamCreateTool | `tools/TeamCreateTool` | ❌ Missing | `ClawSharp.Tools.Agent.TeamCreateTool` |
| TeamDeleteTool | `tools/TeamDeleteTool` | ❌ Missing | `ClawSharp.Tools.Agent.TeamDeleteTool` |
| ToolSearchTool | `tools/ToolSearchTool` | ❌ Missing | `ClawSharp.Tools.Search.ToolSearchTool` |

## Phase 1: Simple Tools
- `SleepTool`
- `BriefTool`
- `SyntheticOutputTool`
- `TodoWriteTool`

## Phase 2: Web & Search
- `WebFetchTool`
- `WebSearchTool`
- `ToolSearchTool`

## Phase 3: Task & Plan Management
- `TaskCreateTool`, `TaskGetTool`, `TaskListTool`, `TaskUpdateTool`
- `EnterPlanModeTool`, `ExitPlanModeTool`, `VerifyPlanExecutionTool`

## Phase 4: Complex & Infrastructure
- `MCP` tools
- `LSPTool`
- `REPLTool`
- `AskUserQuestionTool` (Requires UI integration)
