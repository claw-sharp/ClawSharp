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
| AskUserQuestionTool | `tools/AskUserQuestionTool` | ✅ Ported | `ClawSharp.Tools.Agent.AskUserQuestionTool` |
| BriefTool / SendUserMessage | `tools/BriefTool` | ✅ Alias Added | `ClawSharp.Tools.Agent.SendUserMessageTool` |
| SleepTool | `tools/SleepTool` | ✅ Existing | `ClawSharp.Tools.Agent.SleepTool` |
| SyntheticOutputTool | `tools/SyntheticOutputTool` | ✅ Existing | `ClawSharp.Tools.Agent.StructuredOutputTool` |
| **File** | | | |
| FileReadTool | `tools/FileReadTool` | ✅ Existing | `ClawSharp.Tools.ReadTool` |
| FileWriteTool | `tools/FileWriteTool` | ✅ Existing | `ClawSharp.Tools.WriteTool` |
| FileEditTool | `tools/FileEditTool` | ✅ Existing | `ClawSharp.Tools.EditTool` |
| GlobTool | `tools/GlobTool` | ✅ Existing | `ClawSharp.Tools.GlobTool` |
| NotebookEditTool | `tools/NotebookEditTool` | ✅ Ported | `ClawSharp.Tools.Notebook.NotebookEditTool` |
| **Shell** | | | |
| BashTool | `tools/BashTool` | ✅ Existing | `ClawSharp.Tools.BashTool` |
| PowerShellTool | `tools/PowerShellTool` | ✅ Existing | `ClawSharp.Tools.PowerShellTool` |
| **Search** | | | |
| GrepTool | `tools/GrepTool` | ✅ Existing | `ClawSharp.Tools.GrepTool` |
| ToolSearchTool | `tools/ToolSearchTool` | ✅ Ported | `ClawSharp.Tools.Search.ToolSearchTool` |
| **Web** | | | |
| WebFetchTool | `tools/WebFetchTool` | ✅ Existing | `ClawSharp.Tools.Web.FetchTool` |
| WebSearchTool | `tools/WebSearchTool` | ✅ Existing | `ClawSharp.Tools.Web.SearchTool` |
| WebBrowserTool | `tools/WebBrowserTool` | ⚠️ Partial | `ClawSharp.Tools.WebBrowserTool` |
| **Tasks (Board V2)** | | | |
| TaskCreateTool | `tools/TaskCreateTool` | ✅ Ported | `ClawSharp.Tools.Tasks.TaskCreateTool` |
| TaskGetTool | `tools/TaskGetTool` | ✅ Ported | `ClawSharp.Tools.Tasks.TaskGetTool` |
| TaskListTool | `tools/TaskListTool` | ✅ Ported | `ClawSharp.Tools.Tasks.TaskListTool` |
| TaskUpdateTool | `tools/TaskUpdateTool` | ✅ Ported | `ClawSharp.Tools.Tasks.TaskUpdateTool` |
| TodoWriteTool | `tools/TodoWriteTool` | ✅ Existing | `ClawSharp.Tools.Tasks.TodoWriteTool` |
| **Plan & Worktree** | | | |
| EnterPlanModeTool | `tools/EnterPlanModeTool` | ✅ Ported | `ClawSharp.Tools.Plan.EnterPlanModeTool` |
| ExitPlanModeTool | `tools/ExitPlanModeTool` | ✅ Ported | `ClawSharp.Tools.Plan.ExitPlanModeTool` |
| EnterWorktreeTool | `tools/EnterWorktreeTool` | ✅ Ported | `ClawSharp.Tools.Worktree.EnterWorktreeTool` |
| ExitWorktreeTool | `tools/ExitWorktreeTool` | ✅ Ported | `ClawSharp.Tools.Worktree.ExitWorktreeTool` |
| VerifyPlanExecutionTool | `tools/VerifyPlanExecutionTool` | ✅ Ported | `ClawSharp.Tools.Plan.VerifyPlanExecutionTool` |
| **MCP** | | | |
| ListMcpResourcesTool | `tools/ListMcpResourcesTool` | ✅ Ported | `ClawSharp.Tools.Mcp.ListMcpResourcesTool` |
| ReadMcpResourceTool | `tools/ReadMcpResourceTool` | ✅ Ported | `ClawSharp.Tools.Mcp.ReadMcpResourceTool` |
| MCPTool (Generic) | `tools/MCPTool` | ✅ Ported | `ClawSharp.Infrastructure.McpToolRegistrationService+RegisteredMcpTool` |
| McpAuthTool | `tools/McpAuthTool` | ✅ Ported | `ClawSharp.Tools.Mcp.McpAuthTool` |
| **Other** | | | |
| LSPTool | `tools/LSPTool` | ✅ Ported | `ClawSharp.Tools.Lsp.LspTool` |
| REPLTool | `tools/REPLTool` | ✅ Ported | `ClawSharp.Tools.REPL.REPLTool` |
| ConfigTool | `tools/ConfigTool` | ✅ Ported | `ClawSharp.Tools.ConfigTool` |
| SkillTool | `tools/SkillTool` | ✅ Ported | `ClawSharp.Tools.Skill.SkillTool` |
| RemoteTriggerTool | `tools/RemoteTriggerTool` | ✅ Ported | `ClawSharp.Tools.Agent.RemoteTriggerTool` |
| ScheduleCronTool | `tools/ScheduleCronTool` | ✅ Ported | `ClawSharp.Tools.Agent.CronCreateTool / CronListTool / CronDeleteTool` |

### 10. REPL and Notebook Tools (Complete)
- [x] Create Jupyter Notebook models foripnb files.
- [x] Port `NotebookEditTool` for cell manipulation.
- [x] Implement `REPLTool` for batch execution.
- [x] Integrate `REPL_ONLY_TOOLS` filtering in `ToolRegistry`.

## Roadmap
- [ ] Port `BashTool` (Advanced shell orchestration).
- [ ] Implement `MCPTool` (Model Context Protocol).
- [ ] Implement Advanced Session Bridge for Remote Control.

## Recently Ported (Latest Turn)
- **VerifyPlanExecutionTool**: Local pending-plan verification state and completion marker.
- **LSPTool**: Roslyn-backed code intelligence for .NET workspaces.
- **RemoteTriggerTool**: Claude OAuth and org-UUID backed trigger management.
- **Cron Tools**: `CronCreate`, `CronList`, and `CronDelete` plus a local scheduler that fires due jobs into background session runs.
- **WebBrowserTool**: Pragmatic Playwright-backed development browser tool for navigate/click/type/eval/console/screenshot flows.
- **NotebookEditTool**: Cell-level Jupyter notebook editing.
- **EnterPlanMode / ExitPlanMode**: Strategic state management for planning phases.
- **ToolSearchTool**: Agent tool discovery via keyword scoring.
- **Task Board (V2)**: Full set of `TaskCreate`, `TaskGet`, `TaskList`, `TaskUpdate` for project management.
- **Alias Brief**: Added `Brief` alias to `SendUserMessageTool`.

## Remaining Work Phases

### Phase 4: Complex Infrastructure
- **MCP Ecosystem**: Porting generic MCP tool handling and resource reading.
- **LSP Integration**: Providing language server capabilities (symbols, definitions) to the model.
- **REPL / Runtime**: Implementing an interactive execution environment.

### Phase 5: UI & Specialized Agent Tools
- **AskUserQuestionTool**: Interactive questioning with multi-type support (select, multi-select).
- **Worktree Management**: Ports for multi-branch exploration.
- **Config & Skills**: Dynamic setting management and skill-based tool expansion.
- **WebBrowserTool**: Implemented as a pragmatic Playwright-backed equivalent because the upstream TS source/runtime surface is not present in the checked-out repo.
### 11. Skill Tool and Injected Messages (Complete)
- [x] Port `SkillTool` to resolve discovered `SKILL.md` files from app state.
- [x] Load real skill contents and inject them as meta user messages in the main conversation.
- [x] Update `ToolExecutionResult` and `ToolExecutionRecord` to support `InjectedMessages`.
- [x] Implement message injection in `ModelBackedIterationRunner` and `ToolOrchestrator`.
