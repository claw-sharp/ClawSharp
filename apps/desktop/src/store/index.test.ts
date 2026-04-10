import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockClient = {
  subscribe: vi.fn(),
  connect: vi.fn(),
  listRecentProjects: vi.fn(),
  openProject: vi.fn(),
  listThreads: vi.fn(),
  getThread: vi.fn(),
  createThread: vi.fn(),
  startRun: vi.fn(),
  cancelRun: vi.fn(),
  listChangedFiles: vi.fn(),
  getDiff: vi.fn(),
  openExternalEditor: vi.fn(),
  listDiagnostics: vi.fn(),
  getSettings: vi.fn(),
  updateSettings: vi.fn(),
  listProviders: vi.fn(),
  validateProviderConfig: vi.fn(),
  listPendingApprovals: vi.fn(),
  resolveApproval: vi.fn(),
  pickProjectDirectory: vi.fn(),
};

vi.mock('@/lib/agentHostClient', () => ({
  agentHostClient: mockClient,
}));

describe('useAppStore', () => {
  beforeEach(() => {
    vi.resetModules();
    mockClient.subscribe.mockReset();
    mockClient.connect.mockReset();
    mockClient.listRecentProjects.mockReset();
    mockClient.openProject.mockReset();
    mockClient.listThreads.mockReset();
    mockClient.getThread.mockReset();
    mockClient.createThread.mockReset();
    mockClient.startRun.mockReset();
    mockClient.cancelRun.mockReset();
    mockClient.listChangedFiles.mockReset();
    mockClient.getDiff.mockReset();
    mockClient.openExternalEditor.mockReset();
    mockClient.listDiagnostics.mockReset();
    mockClient.getSettings.mockReset();
    mockClient.updateSettings.mockReset();
    mockClient.listProviders.mockReset();
    mockClient.validateProviderConfig.mockReset();
    mockClient.listPendingApprovals.mockReset();
    mockClient.resolveApproval.mockReset();
    mockClient.pickProjectDirectory.mockReset();
    mockClient.subscribe.mockResolvedValue(() => undefined);
    mockClient.listProviders.mockResolvedValue({
      providers: [
        {
          id: 'anthropic',
          displayName: 'Anthropic',
          defaultModel: 'claude-haiku-4-5-20251001',
          models: ['claude-haiku-4-5-20251001'],
          baseUrl: 'https://api.anthropic.com',
          requiresApiKey: true,
          description: 'Claude default provider selection.',
        },
      ],
    });
    mockClient.getSettings.mockResolvedValue({
      settings: {
        provider: 'anthropic',
        model: 'claude-haiku-4-5-20251001',
        fallbackModel: null,
        permissionMode: 'Default',
        enableTelemetry: true,
        fileCheckpointingEnabled: true,
        baseUrl: 'https://api.anthropic.com',
        transport: 'AnthropicMessages',
        configPath: '/Users/test/.claude/settings.json',
        settingsIssues: [],
        credentials: {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: 'none',
          hasExternalCredential: false,
          externalCredentialPath: null,
        },
        hasAnyConfiguredProviderCredential: false,
      },
    });
    mockClient.validateProviderConfig.mockResolvedValue({
      validation: {
        provider: 'anthropic',
        isValid: true,
        errors: [],
        warnings: [],
      },
    });
    mockClient.listDiagnostics.mockResolvedValue({
      diagnostics: {
        threadId: 'thread-1',
        sessionId: 'thread-1',
        provider: 'anthropic',
        model: 'claude-haiku-4-5-20251001',
        baseUrl: 'https://api.anthropic.com',
        transport: 'AnthropicMessages',
        environment: 'macOS',
        configPath: '/Users/test/.claude/settings.json',
        uptime: '1.0m',
        memoryUsage: '120 MB',
        logPaths: {
          debugLogPath: '/tmp/debug.log',
          telemetryEventsPath: '/tmp/events.jsonl',
          metricsPath: '/tmp/metrics.jsonl',
          crashPath: '/tmp/crash.jsonl',
          tracePath: '/tmp/trace.json',
          startupProfilePath: '/tmp/startup.json',
        },
        warnings: [],
        errors: [],
        recentEvents: [],
      },
    });
    mockClient.listChangedFiles.mockResolvedValue({ projectId: 'proj-1', threadId: 'thread-1', files: [] });
    mockClient.listPendingApprovals.mockResolvedValue({ approvals: [] });
  });

  it('initializes from recent projects and loads the first thread detail', async () => {
    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          lastOpenedAt: '2026-04-08T10:00:00.000Z',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          threadCount: 1,
          gitBranch: 'main',
        },
      ],
    });
    mockClient.listThreads.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          messageCount: 1,
          transcriptPath: '/repo/.claude/thread-1.jsonl',
          worktree: {
            repoRoot: '/repo',
            worktreePath: '/repo',
          },
        },
      ],
    });
    mockClient.getThread.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      thread: {
        thread: {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          messageCount: 1,
          transcriptPath: '/repo/.claude/thread-1.jsonl',
          worktree: {
            repoRoot: '/repo',
            worktreePath: '/repo',
          },
        },
        messages: [
          {
            id: 'msg-1',
            threadId: 'thread-1',
            role: 'user',
            content: 'Hello',
            timestamp: '2026-04-08T10:01:00.000Z',
          },
        ],
      },
    });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();

    const state = useAppStore.getState();
    expect(state.selectedProjectId).toBe('proj-1');
    expect(state.selectedThreadId).toBe('thread-1');
    expect(state.projects[0]?.name).toBe('ClawSharp');
    expect(state.messages['thread-1'][0]?.content).toBe('Hello');
    expect(state.connection.isConnected).toBe(true);
    expect(mockClient.getSettings).toHaveBeenCalledTimes(1);
  });

  it('loads the thread even when ancillary thread requests fail', async () => {
    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({
      projects: [],
    });
    mockClient.getThread.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      thread: {
        thread: {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          messageCount: 1,
          transcriptPath: '/repo/.claude/thread-1.jsonl',
          worktree: {
            repoRoot: '/repo',
            worktreePath: '/repo',
          },
        },
        messages: [
          {
            id: 'msg-1',
            threadId: 'thread-1',
            role: 'user',
            content: 'Hello',
            timestamp: '2026-04-08T10:01:00.000Z',
          },
        ],
      },
    });
    mockClient.listChangedFiles.mockRejectedValue('fatal: not a git repository');
    mockClient.listDiagnostics.mockRejectedValue(new Error('diagnostics unavailable'));
    mockClient.listPendingApprovals.mockRejectedValue({ message: 'approval store unavailable' });

    const { useAppStore } = await import('@/store');
    useAppStore.setState({
      selectedProjectId: 'proj-1',
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdated: '2026-04-08T10:01:00.000Z',
          status: 'idle',
          changedFilesCount: 0,
          target: 'local',
          provider: 'anthropic',
          model: 'claude-haiku-4-5-20251001',
          pinned: false,
        },
      ],
    });

    await useAppStore.getState().selectThread('thread-1');

    const state = useAppStore.getState();
    expect(state.selectedThreadId).toBe('thread-1');
    expect(state.messages['thread-1'][0]?.content).toBe('Hello');
    expect(state.changedFiles['thread-1']).toEqual([]);
    expect(state.connection.errorMessage).toBeNull();
  });

  it('sends a real prompt request through the store and marks the run active', async () => {
    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          lastOpenedAt: '2026-04-08T10:00:00.000Z',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          threadCount: 1,
          gitBranch: 'main',
        },
      ],
    });
    mockClient.listThreads.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          messageCount: 0,
          transcriptPath: '/repo/.claude/thread-1.jsonl',
          worktree: {
            repoRoot: '/repo',
            worktreePath: '/repo',
          },
        },
      ],
    });
    mockClient.getThread.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      thread: {
        thread: {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Thread One',
          summary: 'Summary',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          messageCount: 0,
          transcriptPath: '/repo/.claude/thread-1.jsonl',
          worktree: {
            repoRoot: '/repo',
            worktreePath: '/repo',
          },
        },
        messages: [],
      },
    });
    mockClient.startRun.mockResolvedValue({
      runId: 'run-1',
      threadId: 'thread-1',
      acceptedAt: '2026-04-08T10:02:00.000Z',
    });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();
    await useAppStore.getState().sendPrompt('thread-1', 'Check this repo');

    const state = useAppStore.getState();
    expect(mockClient.startRun).toHaveBeenCalledWith('proj-1', 'thread-1', 'Check this repo');
    expect(state.messages['thread-1'][0]?.content).toBe('Check this repo');
    expect(state.run.activeRunId).toBe('run-1');
    expect(state.run.isRunning).toBe(true);
  });

  it('publishes recent projects before the first project finishes hydrating', async () => {
    let resolveListThreads: ((value: {
      project: {
        id: string;
        name: string;
        path: string;
        lastOpenedAt: string;
        lastUpdatedAt: string;
        threadCount: number;
        gitBranch: string | null;
      };
      threads: never[];
    }) => void) | undefined;

    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          lastOpenedAt: '2026-04-08T10:00:00.000Z',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          threadCount: 1,
          gitBranch: 'main',
        },
      ],
    });
    mockClient.listThreads.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveListThreads = resolve;
        }),
    );

    const { useAppStore } = await import('@/store');
    const initializePromise = useAppStore.getState().initialize();

    await new Promise((resolve) => setTimeout(resolve, 0));

    let state = useAppStore.getState();
    expect(state.projects.map((project) => project.id)).toEqual(['proj-1']);
    expect(state.connection.isBootstrapping).toBe(false);
    expect(state.selectedProjectId).toBe('');

    resolveListThreads?.({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 1,
        gitBranch: 'main',
      },
      threads: [],
    });

    await initializePromise;

    state = useAppStore.getState();
    expect(state.projects[0]?.id).toBe('proj-1');
    expect(state.selectedProjectId).toBe('proj-1');
  });

  it('preserves string startup failures during initialize', async () => {
    mockClient.connect.mockRejectedValue('Failed to start AgentHost using dotnet: program not found');

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();

    const state = useAppStore.getState();
    expect(state.connection.statusLabel).toBe('Connection failed');
    expect(state.connection.errorMessage).toBe('Failed to start AgentHost using dotnet: program not found');
  });

  it('validates provider config with draft credential overrides and stores the returned warnings', async () => {
    mockClient.validateProviderConfig.mockResolvedValue({
      validation: {
        provider: 'openai',
        isValid: true,
        errors: [],
        warnings: ['Validated against the live API.'],
      },
    });

    const { useAppStore } = await import('@/store');
    const result = await useAppStore.getState().validateProviderConfig({
      provider: 'openai',
      model: 'gpt-4o',
      providerApiKey: 'sk-openai-test',
      liveCheck: true,
    });

    expect(mockClient.validateProviderConfig).toHaveBeenCalledWith({
      projectId: null,
      provider: 'openai',
      model: 'gpt-4o',
      liveCheck: true,
      apiKey: 'sk-openai-test',
      authToken: undefined,
      accountId: undefined,
      useExternalCredential: undefined,
    });
    expect(result).toEqual({
      isValid: true,
      warnings: ['Validated against the live API.'],
      errors: [],
    });
    expect(useAppStore.getState().settings.providerValidationWarnings).toEqual(['Validated against the live API.']);
  });

  it('does not overwrite a newly opened project when bootstrap recents resolve late', async () => {
    let resolveRecentProjects: ((value: {
      projects: Array<{
        id: string;
        name: string;
        path: string;
        lastOpenedAt: string;
        lastUpdatedAt: string;
        threadCount: number;
        gitBranch: string | null;
      }>;
    }) => void) | undefined;

    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveRecentProjects = resolve;
        }),
    );
    mockClient.openProject.mockResolvedValue({
      project: {
        id: 'proj-open',
        name: 'Opened Project',
        path: '/opened',
        lastOpenedAt: '2026-04-08T10:02:00.000Z',
        lastUpdatedAt: '2026-04-08T10:02:00.000Z',
        threadCount: 0,
        gitBranch: 'main',
      },
      threads: [],
    });
    mockClient.listThreads.mockImplementation(async (projectId: string) => ({
      project: projectId === 'proj-open'
        ? {
            id: 'proj-open',
            name: 'Opened Project',
            path: '/opened',
            lastOpenedAt: '2026-04-08T10:02:00.000Z',
            lastUpdatedAt: '2026-04-08T10:02:00.000Z',
            threadCount: 0,
            gitBranch: 'main',
          }
        : {
            id: 'proj-recent',
            name: 'Recent Project',
            path: '/recent',
            lastOpenedAt: '2026-04-08T10:00:00.000Z',
            lastUpdatedAt: '2026-04-08T10:01:00.000Z',
            threadCount: 0,
            gitBranch: 'main',
          },
      threads: [],
    }));

    const { useAppStore } = await import('@/store');
    const initializePromise = useAppStore.getState().initialize();

    await Promise.resolve();
    await useAppStore.getState().openProjectPath('/opened');

    resolveRecentProjects?.({
      projects: [
        {
          id: 'proj-recent',
          name: 'Recent Project',
          path: '/recent',
          lastOpenedAt: '2026-04-08T10:00:00.000Z',
          lastUpdatedAt: '2026-04-08T10:01:00.000Z',
          threadCount: 0,
          gitBranch: 'main',
        },
      ],
    });

    await initializePromise;

    const state = useAppStore.getState();
    expect(state.selectedProjectId).toBe('proj-open');
    expect(state.projects.map((project) => project.id)).toEqual(['proj-open', 'proj-recent']);
    expect(mockClient.listThreads).toHaveBeenCalledWith('proj-open');
    expect(mockClient.listThreads).not.toHaveBeenCalledWith('proj-recent');
  });
});
