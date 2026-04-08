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
| WebBrowserTool | `tools/WebBrowserTool` | ❌ Pending | `ClawSharp.Tools.Web.BrowserTool` |
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
| VerifyPlanExecutionTool | `tools/VerifyPlanExecutionTool` | ❌ Pending | `ClawSharp.Tools.Plan.VerifyPlanExecutionTool` |
| **MCP** | | | |
| ListMcpResourcesTool | `tools/ListMcpResourcesTool` | ✅ Ported | `ClawSharp.Tools.Mcp.ListMcpResourcesTool` |
| ReadMcpResourceTool | `tools/ReadMcpResourceTool` | ✅ Ported | `ClawSharp.Tools.Mcp.ReadMcpResourceTool` |
| MCPTool (Generic) | `tools/MCPTool` | ✅ Ported | `ClawSharp.Infrastructure.McpToolRegistrationService+RegisteredMcpTool` |
| McpAuthTool | `tools/McpAuthTool` | ✅ Ported | `ClawSharp.Tools.Mcp.McpAuthTool` |
| **Other** | | | |
| LSPTool | `tools/LSPTool` | ❌ Pending | `ClawSharp.Tools.Lsp.LspTool` |
| REPLTool | `tools/REPLTool` | ✅ Ported | `ClawSharp.Tools.REPL.REPLTool` |
| ConfigTool | `tools/ConfigTool` | ✅ Ported | `ClawSharp.Tools.ConfigTool` |
| SkillTool | `tools/SkillTool` | ✅ Ported (Inline) | `ClawSharp.Tools.Skill.SkillTool` |
| RemoteTriggerTool | `tools/RemoteTriggerTool` | ❌ Pending | `ClawSharp.Tools.Agent.RemoteTriggerTool` |
| ScheduleCronTool | `tools/ScheduleCronTool` | ❌ Pending | `ClawSharp.Tools.Agent.CronTool` |

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
### 11. Skill Tool and Injected Messages (Complete)
- [x] Create `SkillRegistry` and `SkillDefinition` models.
- [x] Port `SkillTool` for named command expansion.
- [x] Update `ToolExecutionResult` and `ToolExecutionRecord` to support `InjectedMessages`.
- [x] Implement message injection in `ModelBackedIterationRunner` and `ToolOrchestrator`.
