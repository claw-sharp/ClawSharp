import { ChangedFile, DiffChunk } from '@/types';

export const mockChangedFiles: Record<string, ChangedFile[]> = {
  'thread-1': [
    { path: 'src/layouts/AppShell.tsx', status: 'A', additions: 142, deletions: 0, threadId: 'thread-1' },
    { path: 'src/components/TopBar.tsx', status: 'A', additions: 67, deletions: 0, threadId: 'thread-1' },
    { path: 'src/components/Sidebar.tsx', status: 'A', additions: 98, deletions: 0, threadId: 'thread-1' },
    { path: 'src/features/review/ReviewPanel.tsx', status: 'A', additions: 85, deletions: 0, threadId: 'thread-1' },
    { path: 'src/features/logs/BottomDrawer.tsx', status: 'A', additions: 54, deletions: 0, threadId: 'thread-1' },
    { path: 'src/App.tsx', status: 'M', additions: 12, deletions: 5, threadId: 'thread-1' },
    { path: 'src/index.css', status: 'M', additions: 45, deletions: 8, threadId: 'thread-1' },
    { path: 'tailwind.config.ts', status: 'M', additions: 18, deletions: 3, threadId: 'thread-1' },
  ],
  'thread-2': [
    { path: 'src/features/settings/ProviderSettings.tsx', status: 'M', additions: 89, deletions: 34, threadId: 'thread-2' },
    { path: 'src/features/settings/ModelSelector.tsx', status: 'A', additions: 56, deletions: 0, threadId: 'thread-2' },
    { path: 'src/features/settings/PresetConfig.tsx', status: 'A', additions: 42, deletions: 0, threadId: 'thread-2' },
    { path: 'src/types/settings.ts', status: 'M', additions: 15, deletions: 8, threadId: 'thread-2' },
  ],
  'thread-3': [
    { path: 'src/features/logs/BottomDrawer.tsx', status: 'A', additions: 78, deletions: 0, threadId: 'thread-3' },
    { path: 'src/features/logs/LogsPane.tsx', status: 'A', additions: 95, deletions: 0, threadId: 'thread-3' },
    { path: 'src/features/logs/TerminalPane.tsx', status: 'A', additions: 62, deletions: 0, threadId: 'thread-3' },
    { path: 'src/features/diagnostics/DiagnosticsPane.tsx', status: 'M', additions: 24, deletions: 45, threadId: 'thread-3' },
    { path: 'src/layouts/AppShell.tsx', status: 'M', additions: 18, deletions: 6, threadId: 'thread-3' },
    { path: 'src/styles/drawer.css', status: 'A', additions: 32, deletions: 0, threadId: 'thread-3' },
  ],
  'thread-6': [
    { path: 'docs/guides/web-workspace.md', status: 'A', additions: 156, deletions: 0, threadId: 'thread-6' },
    { path: 'docs/guides/_sidebar.md', status: 'M', additions: 3, deletions: 0, threadId: 'thread-6' },
    { path: 'docs/assets/web-workspace-screenshot.png', status: 'A', additions: 0, deletions: 0, threadId: 'thread-6' },
  ],
  'thread-8': [
    { path: 'src/components/CodeSandbox.tsx', status: 'A', additions: 87, deletions: 0, threadId: 'thread-8' },
    { path: 'src/components/ExampleRunner.tsx', status: 'A', additions: 45, deletions: 0, threadId: 'thread-8' },
  ],
};

export const mockDiffs: Record<string, DiffChunk> = {
  'src/layouts/AppShell.tsx': {
    filePath: 'src/layouts/AppShell.tsx',
    hunks: [
      {
        header: '@@ -0,0 +1,48 @@',
        lines: [
          { type: 'add', content: "import React from 'react';", newLineNumber: 1 },
          { type: 'add', content: "import { TopBar } from '@/components/TopBar';", newLineNumber: 2 },
          { type: 'add', content: "import { Sidebar } from '@/components/Sidebar';", newLineNumber: 3 },
          { type: 'add', content: "import { ReviewPanel } from '@/features/review/ReviewPanel';", newLineNumber: 4 },
          { type: 'add', content: "import { BottomDrawer } from '@/features/logs/BottomDrawer';", newLineNumber: 5 },
          { type: 'add', content: '', newLineNumber: 6 },
          { type: 'add', content: 'export const AppShell: React.FC = ({ children }) => {', newLineNumber: 7 },
          { type: 'add', content: '  return (', newLineNumber: 8 },
          { type: 'add', content: '    <div className="grid grid-cols-[280px_1fr_350px] grid-rows-[48px_1fr_200px] h-screen">', newLineNumber: 9 },
          { type: 'add', content: '      <TopBar className="col-span-3" />', newLineNumber: 10 },
          { type: 'add', content: '      <Sidebar />', newLineNumber: 11 },
          { type: 'add', content: '      <main className="overflow-auto">{children}</main>', newLineNumber: 12 },
          { type: 'add', content: '      <ReviewPanel />', newLineNumber: 13 },
          { type: 'add', content: '      <BottomDrawer className="col-span-3" />', newLineNumber: 14 },
          { type: 'add', content: '    </div>', newLineNumber: 15 },
          { type: 'add', content: '  );', newLineNumber: 16 },
          { type: 'add', content: '};', newLineNumber: 17 },
        ],
      },
    ],
  },
  'src/App.tsx': {
    filePath: 'src/App.tsx',
    hunks: [
      {
        header: '@@ -1,8 +1,15 @@',
        lines: [
          { type: 'context', content: "import React from 'react';", oldLineNumber: 1, newLineNumber: 1 },
          { type: 'del', content: "import { BasicLayout } from './layouts/BasicLayout';", oldLineNumber: 2 },
          { type: 'add', content: "import { AppShell } from './layouts/AppShell';", newLineNumber: 2 },
          { type: 'add', content: "import { ThreadView } from './features/chat/ThreadView';", newLineNumber: 3 },
          { type: 'context', content: '', oldLineNumber: 3, newLineNumber: 4 },
          { type: 'del', content: 'const App = () => (', oldLineNumber: 4 },
          { type: 'del', content: '  <BasicLayout>', oldLineNumber: 5 },
          { type: 'del', content: '    <h1>ClawSharp</h1>', oldLineNumber: 6 },
          { type: 'del', content: '  </BasicLayout>', oldLineNumber: 7 },
          { type: 'add', content: 'const App = () => (', newLineNumber: 5 },
          { type: 'add', content: '  <AppShell>', newLineNumber: 6 },
          { type: 'add', content: '    <ThreadView />', newLineNumber: 7 },
          { type: 'add', content: '  </AppShell>', newLineNumber: 8 },
          { type: 'context', content: ');', oldLineNumber: 8, newLineNumber: 9 },
        ],
      },
    ],
  },
  'src/features/settings/ProviderSettings.tsx': {
    filePath: 'src/features/settings/ProviderSettings.tsx',
    hunks: [
      {
        header: '@@ -12,18 +12,42 @@',
        lines: [
          { type: 'context', content: "import { Card } from '@/components/ui/Card';", oldLineNumber: 12, newLineNumber: 12 },
          { type: 'context', content: '', oldLineNumber: 13, newLineNumber: 13 },
          { type: 'del', content: 'export const ProviderSettings = () => {', oldLineNumber: 14 },
          { type: 'del', content: '  const [provider, setProvider] = useState("anthropic");', oldLineNumber: 15 },
          { type: 'add', content: 'interface ProviderConfig {', newLineNumber: 14 },
          { type: 'add', content: '  id: string;', newLineNumber: 15 },
          { type: 'add', content: '  name: string;', newLineNumber: 16 },
          { type: 'add', content: '  models: string[];', newLineNumber: 17 },
          { type: 'add', content: '  requiresApiKey: boolean;', newLineNumber: 18 },
          { type: 'add', content: '}', newLineNumber: 19 },
          { type: 'add', content: '', newLineNumber: 20 },
          { type: 'add', content: 'const PROVIDERS: ProviderConfig[] = [', newLineNumber: 21 },
          { type: 'add', content: '  { id: "anthropic", name: "Anthropic", models: ["claude-4-sonnet", "claude-4-opus"], requiresApiKey: true },', newLineNumber: 22 },
          { type: 'add', content: '  { id: "openai", name: "OpenAI", models: ["o3", "gpt-4.1"], requiresApiKey: true },', newLineNumber: 23 },
          { type: 'add', content: '];', newLineNumber: 24 },
        ],
      },
    ],
  },
};
