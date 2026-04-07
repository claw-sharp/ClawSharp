import { create } from 'zustand';
import { UIState, RunState, Message, ToolProgressEvent, SettingsState } from '@/types';
import { mockProjects } from '@/mocks/projects';
import { mockThreads } from '@/mocks/threads';
import { mockMessages } from '@/mocks/messages';
import { mockChangedFiles } from '@/mocks/diffs';
import { mockLogs, mockTerminalOutput } from '@/mocks/logs';
import { mockDiagnostics } from '@/mocks/diagnostics';
import { mockAutomations } from '@/mocks/automations';
import { mockInboxItems } from '@/mocks/inbox';

interface AppStore {
  // Data
  projects: typeof mockProjects;
  threads: typeof mockThreads;
  messages: Record<string, Message[]>;
  changedFiles: typeof mockChangedFiles;
  logs: typeof mockLogs;
  terminalOutput: typeof mockTerminalOutput;
  diagnostics: typeof mockDiagnostics;
  automations: typeof mockAutomations;
  inboxItems: typeof mockInboxItems;

  // Selection
  selectedProjectId: string;
  selectedThreadId: string;

  // UI
  ui: UIState;
  run: RunState;
  settings: SettingsState;

  // Actions
  selectProject: (id: string) => void;
  selectThread: (id: string) => void;
  toggleLeftSidebar: () => void;
  toggleBottomDrawer: () => void;
  setBottomDrawerTab: (tab: UIState['bottomDrawerTab']) => void;
  setRightPanelTab: (tab: UIState['rightPanelTab']) => void;
  setActiveView: (view: UIState['activeView']) => void;
  selectChangedFile: (path: string | null) => void;
  toggleSettings: () => void;
  toggleCommandPalette: () => void;
  markInboxItemRead: (id: string) => void;
  sendMockPrompt: (threadId: string, content: string) => void;
  cancelMockRun: () => void;
  updateSettings: (partial: Partial<SettingsState>) => void;
}

const defaultSettings: SettingsState = {
  theme: 'dark',
  density: 'comfortable',
  defaultProvider: 'anthropic',
  defaultModel: 'claude-4-sonnet',
  showDiagnostics: true,
  streamingSpeed: 'normal',
  compactMode: false,
  reducedMotion: false,
  notifications: true,
  editorPath: '/usr/local/bin/code',
};

export const useAppStore = create<AppStore>((set, get) => ({
  projects: mockProjects,
  threads: mockThreads,
  messages: { ...mockMessages },
  changedFiles: mockChangedFiles,
  logs: mockLogs,
  terminalOutput: mockTerminalOutput,
  diagnostics: mockDiagnostics,
  automations: mockAutomations,
  inboxItems: mockInboxItems,

  selectedProjectId: 'proj-1',
  selectedThreadId: 'thread-1',

  ui: {
    leftSidebarCollapsed: false,
    rightPanelTab: 'files',
    bottomDrawerTab: 'logs',
    bottomDrawerOpen: true,
    settingsOpen: false,
    commandPaletteOpen: false,
    selectedChangedFile: null,
    selectedInboxItem: null,
    activeView: 'threads',
  },

  run: {
    activeRunId: null,
    isRunning: false,
    isStreaming: false,
    pendingApproval: false,
    progressLabel: '',
    changedFilesCount: 0,
  },

  settings: defaultSettings,

  selectProject: (id) => {
    const threads = get().threads.filter(t => t.projectId === id);
    const firstThread = threads[0]?.id || '';
    set({
      selectedProjectId: id,
      selectedThreadId: firstThread,
      ui: { ...get().ui, activeView: 'threads' },
    });
  },

  selectThread: (id) => set({ selectedThreadId: id }),

  toggleLeftSidebar: () => set(s => ({
    ui: { ...s.ui, leftSidebarCollapsed: !s.ui.leftSidebarCollapsed },
  })),

  toggleBottomDrawer: () => set(s => ({
    ui: { ...s.ui, bottomDrawerOpen: !s.ui.bottomDrawerOpen },
  })),

  setBottomDrawerTab: (tab) => set(s => ({
    ui: { ...s.ui, bottomDrawerTab: tab, bottomDrawerOpen: true },
  })),

  setRightPanelTab: (tab) => set(s => ({
    ui: { ...s.ui, rightPanelTab: tab },
  })),

  setActiveView: (view) => set(s => ({
    ui: { ...s.ui, activeView: view },
  })),

  selectChangedFile: (path) => set(s => ({
    ui: { ...s.ui, selectedChangedFile: path, rightPanelTab: path ? 'diff' : 'files' },
  })),

  toggleSettings: () => set(s => ({
    ui: { ...s.ui, settingsOpen: !s.ui.settingsOpen },
  })),

  toggleCommandPalette: () => set(s => ({
    ui: { ...s.ui, commandPaletteOpen: !s.ui.commandPaletteOpen },
  })),

  markInboxItemRead: (id) => set(s => ({
    inboxItems: s.inboxItems.map(item =>
      item.id === id ? { ...item, read: true } : item
    ),
  })),

  sendMockPrompt: (threadId, content) => {
    const userMsg: Message = {
      id: `msg-${Date.now()}`,
      threadId,
      role: 'user',
      content,
      timestamp: new Date().toISOString(),
    };

    set(s => ({
      messages: {
        ...s.messages,
        [threadId]: [...(s.messages[threadId] || []), userMsg],
      },
      run: { ...s.run, isRunning: true, isStreaming: true, progressLabel: 'Analyzing prompt...' },
    }));

    // Simulate agent response
    const stages: { label: string; type: ToolProgressEvent['type']; delay: number }[] = [
      { label: 'Reading relevant files', type: 'reading', delay: 800 },
      { label: 'Planning changes', type: 'planning', delay: 1500 },
      { label: 'Implementing changes', type: 'editing', delay: 2500 },
      { label: 'Reviewing modifications', type: 'reviewing', delay: 3500 },
      { label: 'Finalizing', type: 'finalizing', delay: 4200 },
    ];

    const toolProgress: ToolProgressEvent[] = [];

    stages.forEach(({ label, type, delay }) => {
      setTimeout(() => {
        if (!get().run.isRunning) return;
        const tp: ToolProgressEvent = {
          id: `tp-${Date.now()}`,
          type,
          label,
          timestamp: new Date().toISOString(),
          completed: false,
        };
        toolProgress.push(tp);
        set(s => ({
          run: { ...s.run, progressLabel: label },
        }));
      }, delay);
    });

    // Complete after streaming
    setTimeout(() => {
      if (!get().run.isRunning) return;
      const completedProgress = toolProgress.map(tp => ({ ...tp, completed: true }));
      const assistantMsg: Message = {
        id: `msg-${Date.now()}-resp`,
        threadId,
        role: 'assistant',
        content: `I've analyzed your request and implemented the changes. Here's what I did:\n\n1. Read the relevant project files to understand the current structure\n2. Planned the necessary modifications\n3. Applied the changes across the affected files\n4. Verified type checking and imports\n\nThe changes are ready for your review in the diff panel. Let me know if you'd like any adjustments.`,
        timestamp: new Date().toISOString(),
        toolProgress: completedProgress,
      };

      set(s => ({
        messages: {
          ...s.messages,
          [threadId]: [...(s.messages[threadId] || []), assistantMsg],
        },
        run: {
          ...s.run,
          isRunning: false,
          isStreaming: false,
          progressLabel: '',
          activeRunId: null,
        },
      }));
    }, 5000);
  },

  cancelMockRun: () => set(s => ({
    run: {
      ...s.run,
      isRunning: false,
      isStreaming: false,
      progressLabel: '',
      activeRunId: null,
    },
  })),

  updateSettings: (partial) => set(s => ({
    settings: { ...s.settings, ...partial },
  })),
}));
