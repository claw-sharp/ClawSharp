import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ThreadView } from '@/features/chat/ThreadView';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

const mockedUseAppStore = vi.mocked(useAppStore);
const baseStoreState = {
  selectedProjectId: '',
  selectedThreadId: '',
  projects: [],
  threads: [],
  messages: {},
  threadHistory: {},
  inboxItems: [],
  run: {
    activeRunId: null,
    activeThreadId: null,
    isRunning: false,
    isStreaming: false,
    pendingApproval: false,
    progressLabel: '',
    changedFilesCount: 0,
    toolProgress: [],
    errorMessage: null,
  },
  connection: {
    isConnected: false,
    isBootstrapping: false,
    lastEventAt: null,
    errorMessage: null,
    statusLabel: 'Disconnected',
  },
  settings: {
    theme: 'dark',
    density: 'comfortable',
    defaultProvider: 'anthropic',
    defaultModel: 'claude-haiku-4-5-20251001',
    fallbackModel: null,
    permissionMode: 'Default',
    providerBaseUrl: '',
    providerTransport: 'AnthropicMessages',
    configPath: '',
    settingsIssues: [],
    providerValidationWarnings: [],
    providerValidationErrors: [],
    availableProviders: [],
    providerCredentials: {
      hasApiKey: false,
      hasAuthToken: false,
      accountId: null,
      source: 'none',
      hasExternalCredential: false,
      externalCredentialPath: null,
    },
    hasAnyConfiguredProviderCredential: false,
    showDiagnostics: true,
    streamingSpeed: 'normal',
    compactMode: false,
    reducedMotion: false,
    notifications: true,
    editorPath: '/usr/local/bin/code',
  },
  createThread: vi.fn(),
  openProjectPicker: vi.fn(),
  toggleSettings: vi.fn(),
  sendPrompt: vi.fn(),
  cancelRun: vi.fn(),
  retryThread: vi.fn(),
  archiveThread: vi.fn(),
  resolveApproval: vi.fn(),
  setActiveView: vi.fn(),
  loadOlderThreadMessages: vi.fn(),
  openExternalEditor: vi.fn(),
};

describe('ThreadView', () => {
  beforeEach(() => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
    } as ReturnType<typeof useAppStore>);
  });

  it('shows a loading state while the agent host is bootstrapping', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      connection: {
        isConnected: false,
        isBootstrapping: true,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connecting to AgentHost...',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    expect(screen.getByText('Starting AgentHost...')).toBeInTheDocument();
    expect(screen.getByText('Connecting to AgentHost...')).toBeInTheDocument();
    expect(screen.queryByText('Open Project')).not.toBeInTheDocument();
  });

  it('prompts the user to configure provider keys when none are configured', () => {
    const toggleSettings = vi.fn();
    const openProjectPicker = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
      toggleSettings,
      openProjectPicker,
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    expect(screen.getByRole('button', { name: 'Open Project' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Configure Provider Keys' }));

    expect(screen.getByText('No provider credentials are configured yet. Add a provider key before starting a thread.')).toBeInTheDocument();
    expect(toggleSettings).toHaveBeenCalledTimes(1);
    expect(openProjectPicker).not.toHaveBeenCalled();
  });

  it('does not prompt to configure provider keys when startup settings already include credentials', () => {
    const openProjectPicker = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected · no project open',
      },
      openProjectPicker,
      settings: {
        ...baseStoreState.settings,
        defaultProvider: 'codex',
        defaultModel: 'gpt-5.4',
        hasAnyConfiguredProviderCredential: true,
        providerCredentials: {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: 'external',
          hasExternalCredential: true,
          externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
        },
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    expect(screen.queryByRole('button', { name: 'Configure Provider Keys' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Project' })).not.toBeDisabled();
  });

  it('prompts for provider keys when a project is open but no thread is selected', () => {
    const toggleSettings = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: 'D:/Working/ClawSharp',
          activeThreadCount: 0,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
      toggleSettings,
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    fireEvent.click(screen.getByRole('button', { name: 'Configure Provider Keys' }));

    expect(screen.getByText('No thread selected for ClawSharp.')).toBeInTheDocument();
    expect(toggleSettings).toHaveBeenCalledTimes(1);
  });

  it('renders tool activity inside the chat transcript even when the assistant has no text yet', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: 'D:/Working/ClawSharp',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Tool visibility',
          summary: 'Testing tool call rendering',
          status: 'running',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: '',
            timestamp: '2026-04-10T10:00:10Z',
            toolProgress: [
              {
                id: 'tool-1',
                type: 'tool',
                toolName: 'functions.exec_command',
                label: 'Ran `rg --files`',
                detail: 'src/App.tsx\nsrc/main.tsx',
                timestamp: '2026-04-10T10:00:10Z',
                completed: true,
                status: 'completed',
              },
            ],
          },
        ],
      },
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    expect(screen.getByText('Tool activity')).toBeInTheDocument();
    expect(screen.queryByText('functions.exec_command')).not.toBeInTheDocument();
    expect(screen.queryByText('Ran `rg --files`')).not.toBeInTheDocument();
    expect(screen.queryByText(/src\/App\.tsx/)).not.toBeInTheDocument();
    expect(screen.queryByText(/src\/main\.tsx/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /tool activity/i }));

    expect(screen.getByText('functions.exec_command')).toBeInTheDocument();
    expect(screen.getByText('Ran `rg --files`')).toBeInTheDocument();
    expect(screen.getByText(/src\/App\.tsx/)).toBeInTheDocument();
    expect(screen.getByText(/src\/main\.tsx/)).toBeInTheDocument();
  });


  it('opens file links in chat messages in the external editor', () => {
    const openExternalEditor = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      openExternalEditor,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Open file link',
          summary: 'Testing clickable file links',
          status: 'idle',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: 'See apps/desktop/src/layouts/AppShell.tsx:42:7 for the layout.',
            timestamp: '2026-04-10T10:00:10Z',
          },
        ],
      },
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    fireEvent.click(screen.getByRole('button', { name: 'apps/desktop/src/layouts/AppShell.tsx:42:7' }));

    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'position',
      path: '/repo/apps/desktop/src/layouts/AppShell.tsx',
      line: 42,
      column: 7,
      editorCommand: '/usr/local/bin/code',
    });
  });

  it('opens backticked file paths in chat messages in the external editor', () => {
    const openExternalEditor = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      openExternalEditor,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Open code file link',
          summary: 'Testing clickable backticked file links',
          status: 'idle',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: 'Verified: `apps/desktop/src/features/chat/ThreadView.test.tsx` passes.',
            timestamp: '2026-04-10T10:00:10Z',
          },
        ],
      },
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    fireEvent.click(screen.getByRole('button', { name: 'apps/desktop/src/features/chat/ThreadView.test.tsx' }));

    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'position',
      path: '/repo/apps/desktop/src/features/chat/ThreadView.test.tsx',
      line: null,
      column: null,
      editorCommand: '/usr/local/bin/code',
    });
  });

  it('opens markdown file links in chat messages in the external editor', () => {
    const openExternalEditor = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      openExternalEditor,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Open markdown file link',
          summary: 'Testing clickable markdown file links',
          status: 'idle',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: 'Verified: [ThreadView.test.tsx](</Users/hadoan/Documents/GitHub/ClawSharp/apps/desktop/src/features/chat/ThreadView.test.tsx:281>) passes.',
            timestamp: '2026-04-10T10:00:10Z',
          },
        ],
      },
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    fireEvent.click(screen.getByRole('button', { name: 'ThreadView.test.tsx' }));

    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'position',
      path: '/Users/hadoan/Documents/GitHub/ClawSharp/apps/desktop/src/features/chat/ThreadView.test.tsx',
      line: 281,
      column: null,
      editorCommand: '/usr/local/bin/code',
    });
  });

  it('renders approval required after transcript messages', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: 'D:/Working/ClawSharp',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Approval order',
          summary: 'Testing approval placement',
          status: 'waiting_approval',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: 'I can fix this safely.',
            timestamp: '2026-04-10T10:00:10Z',
          },
        ],
      },
      inboxItems: [
        {
          id: 'approval-1',
          type: 'review',
          title: 'Approval required',
          summary: 'Approve the git rewrite.',
          timestamp: '2026-04-10T10:00:20Z',
          read: false,
          projectId: 'proj-1',
          threadId: 'thread-1',
          approvalId: 'approval-1',
        },
      ],
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    const transcriptMessage = screen.getByText('I can fix this safely.');
    const approvalSummary = screen.getByText('Approve the git rewrite.');
    expect(screen.getByRole('button', { name: 'Always Allow This Session' })).toBeInTheDocument();

    expect(
      transcriptMessage.compareDocumentPosition(approvalSummary) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('shows a context badge based on messages after the latest compaction boundary', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      selectedThreadId: 'thread-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: 'D:/Working/ClawSharp',
          activeThreadCount: 1,
          lastUpdated: '2026-04-10T10:00:00Z',
        },
      ],
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Context badge',
          summary: 'Testing composer context usage',
          status: 'completed',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-10T10:00:00Z',
          provider: 'openai',
          model: 'codex',
          pinned: false,
        },
      ],
      messages: {
        'thread-1': [
          {
            id: 'user-old',
            threadId: 'thread-1',
            role: 'user',
            content: 'x'.repeat(8_000),
            timestamp: '2026-04-10T10:00:00Z',
          },
          {
            id: 'boundary-1',
            threadId: 'thread-1',
            role: 'system',
            content: 'Conversation compacted',
            timestamp: '2026-04-10T10:00:05Z',
          },
          {
            id: 'assistant-1',
            threadId: 'thread-1',
            role: 'assistant',
            content: 'y'.repeat(4_000),
            timestamp: '2026-04-10T10:00:10Z',
          },
        ],
      },
      settings: {
        ...baseStoreState.settings,
        hasAnyConfiguredProviderCredential: true,
      },
      connection: {
        isConnected: true,
        isBootstrapping: false,
        lastEventAt: null,
        errorMessage: null,
        statusLabel: 'Connected',
      },
    } as ReturnType<typeof useAppStore>);

    render(<ThreadView />);

    expect(screen.getByRole('button', { name: 'Context ~1%' })).toBeInTheDocument();

  });
});
