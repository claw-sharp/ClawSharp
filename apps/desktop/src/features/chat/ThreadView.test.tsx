import { render, screen } from '@testing-library/react';
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
    showDiagnostics: true,
    streamingSpeed: 'normal',
    compactMode: false,
    reducedMotion: false,
    notifications: true,
    editorPath: '/usr/local/bin/code',
  },
  createThread: vi.fn(),
  openProjectPicker: vi.fn(),
  sendPrompt: vi.fn(),
  cancelRun: vi.fn(),
  retryThread: vi.fn(),
  archiveThread: vi.fn(),
  resolveApproval: vi.fn(),
  setActiveView: vi.fn(),
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
});
