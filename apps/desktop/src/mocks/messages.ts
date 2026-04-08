import { Message } from '@/types';

export const mockMessages: Record<string, Message[]> = {
  'thread-1': [
    {
      id: 'msg-1-1',
      threadId: 'thread-1',
      role: 'user',
      content: 'Create the main app shell for ClawSharp Web. I need a layout with a left sidebar for projects and threads, a center panel for the active conversation, a right panel for file review, and a bottom drawer for logs and diagnostics.',
      timestamp: '2026-04-07T10:00:00Z',
    },
    {
      id: 'msg-1-2',
      threadId: 'thread-1',
      role: 'assistant',
      content: "I'll build the app shell with a responsive layout. Let me start by analyzing the current project structure and then scaffold the components.\n\nHere's my plan:\n1. Create the `AppShell` layout component with CSS Grid\n2. Build the `TopBar` with project context and actions\n3. Implement the collapsible `Sidebar` with project/thread lists\n4. Set up the center `ThreadView` panel\n5. Add the right `ReviewPanel` for file diffs\n6. Create the bottom `BottomDrawer` with tabs for logs, terminal, and diagnostics\n\nLet me start implementing this now.",
      timestamp: '2026-04-07T10:00:15Z',
      toolProgress: [
        { id: 'tp-1', type: 'reading', label: 'Reading project structure', detail: 'Scanning src/ directory', timestamp: '2026-04-07T10:00:16Z', completed: true },
        { id: 'tp-2', type: 'planning', label: 'Planning layout architecture', detail: 'CSS Grid with 4 regions', timestamp: '2026-04-07T10:00:20Z', completed: true },
        { id: 'tp-3', type: 'editing', label: 'Creating AppShell.tsx', detail: 'Main layout component', timestamp: '2026-04-07T10:00:25Z', completed: true },
        { id: 'tp-4', type: 'editing', label: 'Creating TopBar.tsx', detail: 'Navigation and status bar', timestamp: '2026-04-07T10:00:30Z', completed: true },
        { id: 'tp-5', type: 'editing', label: 'Creating Sidebar.tsx', detail: 'Project and thread lists', timestamp: '2026-04-07T10:00:35Z', completed: true },
        { id: 'tp-6', type: 'reviewing', label: 'Reviewing changes', detail: '8 files modified', timestamp: '2026-04-07T10:00:40Z', completed: true },
      ],
    },
    {
      id: 'msg-1-3',
      threadId: 'thread-1',
      role: 'user',
      content: 'Looks great. Can you also add keyboard shortcuts for toggling the sidebar and bottom drawer? Use Cmd+B for sidebar and Cmd+J for the drawer.',
      timestamp: '2026-04-07T10:15:00Z',
    },
    {
      id: 'msg-1-4',
      threadId: 'thread-1',
      role: 'assistant',
      content: "I've added keyboard shortcut handlers to the AppShell component. `⌘B` toggles the left sidebar and `⌘J` toggles the bottom drawer. I also added `⌘K` for the command palette.\n\nThe shortcuts are registered via a `useEffect` hook with proper cleanup. They work globally and don't interfere with text input fields.",
      timestamp: '2026-04-07T10:15:20Z',
      toolProgress: [
        { id: 'tp-7', type: 'editing', label: 'Adding keyboard handlers', detail: 'useEffect with event listeners', timestamp: '2026-04-07T10:15:21Z', completed: true },
        { id: 'tp-8', type: 'testing', label: 'Verifying shortcuts', detail: 'No conflicts with browser defaults', timestamp: '2026-04-07T10:15:25Z', completed: true },
      ],
    },
  ],
  'thread-2': [
    {
      id: 'msg-2-1',
      threadId: 'thread-2',
      role: 'user',
      content: 'Redesign the provider settings panel. It should have a clean form with provider selection, API key input, model selection, and preset configurations.',
      timestamp: '2026-04-07T07:00:00Z',
    },
    {
      id: 'msg-2-2',
      threadId: 'thread-2',
      role: 'assistant',
      content: "Done! I've redesigned the provider settings with:\n- A tabbed interface for different providers (Anthropic, OpenAI, local)\n- Secure API key input with show/hide toggle\n- Model selector with capability badges\n- Preset configurations (Fast, Balanced, Thorough)\n- Validation feedback for all fields\n\nAll 4 changed files have been committed to the worktree.",
      timestamp: '2026-04-07T07:02:30Z',
      toolProgress: [
        { id: 'tp-9', type: 'reading', label: 'Reading current settings', completed: true, timestamp: '2026-04-07T07:00:10Z' },
        { id: 'tp-10', type: 'editing', label: 'Redesigning SettingsPanel', completed: true, timestamp: '2026-04-07T07:01:00Z' },
        { id: 'tp-11', type: 'editing', label: 'Adding validation logic', completed: true, timestamp: '2026-04-07T07:01:30Z' },
        { id: 'tp-12', type: 'finalizing', label: 'Committing changes', completed: true, timestamp: '2026-04-07T07:02:20Z' },
      ],
    },
  ],
  'thread-3': [
    {
      id: 'msg-3-1',
      threadId: 'thread-3',
      role: 'user',
      content: 'Move the diagnostics view into a tabbed bottom drawer alongside logs and terminal output.',
      timestamp: '2026-04-07T09:00:00Z',
    },
    {
      id: 'msg-3-2',
      threadId: 'thread-3',
      role: 'assistant',
      content: "I've refactored the diagnostics into the bottom drawer. Here's what changed:\n\n1. Created a `BottomDrawer` component with three tabs: Logs, Terminal, Diagnostics\n2. Moved the diagnostics panel content into a dedicated `DiagnosticsPane`\n3. Added a `LogsPane` with timestamped, filterable entries\n4. Created a `TerminalPane` with monospace output and scroll-to-bottom\n5. Updated the layout to support a resizable drawer height\n6. Added `⌘J` shortcut to toggle the drawer\n\nThis needs your review before merging — there are 6 files changed with some layout adjustments that affect the main shell.",
      timestamp: '2026-04-07T09:10:00Z',
      toolProgress: [
        { id: 'tp-13', type: 'reading', label: 'Analyzing current layout', completed: true, timestamp: '2026-04-07T09:00:10Z' },
        { id: 'tp-14', type: 'planning', label: 'Designing drawer architecture', completed: true, timestamp: '2026-04-07T09:01:00Z' },
        { id: 'tp-15', type: 'editing', label: 'Creating BottomDrawer', completed: true, timestamp: '2026-04-07T09:03:00Z' },
        { id: 'tp-16', type: 'editing', label: 'Moving DiagnosticsPane', completed: true, timestamp: '2026-04-07T09:05:00Z' },
        { id: 'tp-17', type: 'editing', label: 'Creating LogsPane', completed: true, timestamp: '2026-04-07T09:07:00Z' },
        { id: 'tp-18', type: 'reviewing', label: 'Reviewing all changes', completed: true, timestamp: '2026-04-07T09:09:00Z' },
      ],
    },
  ],
  'thread-6': [
    {
      id: 'msg-6-1',
      threadId: 'thread-6',
      role: 'user',
      content: 'Write a getting started guide for the web workspace. Cover installation, first project setup, and basic agent interaction.',
      timestamp: '2026-04-07T09:00:00Z',
    },
    {
      id: 'msg-6-2',
      threadId: 'thread-6',
      role: 'assistant',
      content: "I'm working on the guide now. I'll structure it as:\n1. Prerequisites and installation\n2. Creating your first project\n3. Starting an agent thread\n4. Reviewing changes\n5. Managing multiple threads",
      timestamp: '2026-04-07T09:00:30Z',
      isStreaming: true,
      toolProgress: [
        { id: 'tp-19', type: 'reading', label: 'Reading existing docs', completed: true, timestamp: '2026-04-07T09:00:15Z' },
        { id: 'tp-20', type: 'editing', label: 'Writing guide content', completed: false, timestamp: '2026-04-07T09:00:30Z' },
      ],
    },
  ],
  'thread-8': [
    {
      id: 'msg-8-1',
      threadId: 'thread-8',
      role: 'user',
      content: 'Add interactive code examples that users can edit and run in the playground.',
      timestamp: '2026-04-06T18:00:00Z',
    },
    {
      id: 'msg-8-2',
      threadId: 'thread-8',
      role: 'assistant',
      content: "I ran into an issue while setting up the code execution sandbox. The playground needs a secure iframe-based runner, but the current CSP headers block cross-origin script execution.\n\nError: `Refused to execute inline script because it violates Content Security Policy`\n\nI'll need to adjust the CSP configuration or use a different sandboxing approach. Let me know if you'd like me to try a Web Worker-based approach instead.",
      timestamp: '2026-04-06T18:10:00Z',
      toolProgress: [
        { id: 'tp-21', type: 'editing', label: 'Creating sandbox component', completed: true, timestamp: '2026-04-06T18:02:00Z' },
        { id: 'tp-22', type: 'testing', label: 'Testing execution', completed: true, timestamp: '2026-04-06T18:08:00Z' },
      ],
    },
  ],
};
