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

  it('initializes from recent projects and loads the first thread detail in the background', async () => {
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

    let state = useAppStore.getState();
    expect(state.selectedProjectId).toBe('proj-1');
    expect(state.projects[0]?.name).toBe('ClawSharp');
    expect(state.connection.isConnected).toBe(true);
    expect(state.ui.navigationLoading).toBeNull();

    await new Promise((resolve) => setTimeout(resolve, 0));

    state = useAppStore.getState();
    expect(state.selectedProjectId).toBe('proj-1');
    expect(state.selectedThreadId).toBe('thread-1');
    expect(state.messages['thread-1'][0]?.content).toBe('Hello');
    expect(mockClient.getSettings).toHaveBeenCalledTimes(2);
    expect(mockClient.getSettings).toHaveBeenCalledWith(null);
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

  it('tracks navigation loading while a thread is being hydrated', async () => {
    let resolveGetThread: ((value: {
      project: {
        id: string;
        name: string;
        path: string;
        lastOpenedAt: string;
        lastUpdatedAt: string;
        threadCount: number;
        gitBranch: string | null;
      };
      thread: {
        thread: {
          id: string;
          projectId: string;
          title: string;
          summary: string;
          lastUpdatedAt: string;
          messageCount: number;
          transcriptPath: string;
          worktree: {
            repoRoot: string;
            worktreePath: string;
          };
        };
        messages: Array<{
          id: string;
          threadId: string;
          role: string;
          content: string;
          timestamp: string;
        }>;
      };
    }) => void) | undefined;

    mockClient.getThread.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveGetThread = resolve;
        }),
    );

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

    const selectPromise = useAppStore.getState().selectThread('thread-1');

    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(useAppStore.getState().ui.navigationLoading).toMatchObject({
      kind: 'thread',
      title: 'Loading thread',
      description: 'Refreshing transcript and review data for Thread One.',
    });

    resolveGetThread?.({
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

    await selectPromise;

    expect(useAppStore.getState().ui.navigationLoading).toBeNull();
  });

  it('clears thread navigation loading once the transcript is loaded even if ancillary requests hang', async () => {
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
    mockClient.listChangedFiles.mockImplementation(() => new Promise(() => undefined));
    mockClient.listDiagnostics.mockImplementation(() => new Promise(() => undefined));
    mockClient.listPendingApprovals.mockImplementation(() => new Promise(() => undefined));

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
    expect(state.ui.navigationLoading).toBeNull();
    expect(state.selectedThreadId).toBe('thread-1');
    expect(state.messages['thread-1'][0]?.content).toBe('Hello');
  });

  it('loads thread messages in pages and prepends older messages on demand', async () => {
    mockClient.getThread.mockImplementation(async (_projectId: string, _threadId: string, options?: {
      beforeMessageId?: string | null;
      pageSize?: number | null;
    }) => {
      if (options?.beforeMessageId === 'msg-2') {
        return {
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
              messageCount: 3,
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
                content: 'First',
                timestamp: '2026-04-08T10:00:00.000Z',
              },
            ],
            hasMoreMessages: false,
            nextBeforeMessageId: null,
          },
        };
      }

      return {
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
            messageCount: 3,
            transcriptPath: '/repo/.claude/thread-1.jsonl',
            worktree: {
              repoRoot: '/repo',
              worktreePath: '/repo',
            },
          },
          messages: [
            {
              id: 'msg-2',
              threadId: 'thread-1',
              role: 'assistant',
              content: 'Second',
              timestamp: '2026-04-08T10:01:00.000Z',
            },
            {
              id: 'msg-3',
              threadId: 'thread-1',
              role: 'user',
              content: 'Third',
              timestamp: '2026-04-08T10:02:00.000Z',
            },
          ],
          hasMoreMessages: true,
          nextBeforeMessageId: 'msg-2',
        },
      };
    });

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

    await useAppStore.getState().selectThread('thread-1', { showLoading: false });

    let state = useAppStore.getState();
    expect(state.messages['thread-1'].map((message) => message.content)).toEqual(['Second', 'Third']);
    expect(state.threadHistory['thread-1']).toMatchObject({
      hasMoreMessages: true,
      nextBeforeMessageId: 'msg-2',
    });

    await useAppStore.getState().loadOlderThreadMessages('thread-1');

    state = useAppStore.getState();
    expect(state.messages['thread-1'].map((message) => message.content)).toEqual(['First', 'Second', 'Third']);
    expect(state.threadHistory['thread-1']).toMatchObject({
      hasMoreMessages: false,
      nextBeforeMessageId: null,
      isLoadingOlder: false,
    });
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
    expect(state.selectedProjectId).toBe('proj-1');
    expect(state.ui.navigationLoading).toBeNull();

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

  it('keeps navigation loading active until the first thread finishes loading for a project', async () => {
    let resolveGetThread: ((value: {
      project: {
        id: string;
        name: string;
        path: string;
        lastOpenedAt: string;
        lastUpdatedAt: string;
        threadCount: number;
        gitBranch: string | null;
      };
      thread: {
        thread: {
          id: string;
          projectId: string;
          title: string;
          summary: string;
          lastUpdatedAt: string;
          messageCount: number;
          transcriptPath: string;
          worktree: {
            repoRoot: string;
            worktreePath: string;
          };
        };
        messages: never[];
      };
    }) => void) | undefined;

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
    mockClient.getThread.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveGetThread = resolve;
        }),
    );

    const { useAppStore } = await import('@/store');
    useAppStore.setState({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          activeThreadCount: 1,
          lastUpdated: '2026-04-08T10:01:00.000Z',
        },
      ],
    });

    const selectPromise = useAppStore.getState().selectProject('proj-1');

    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(useAppStore.getState().ui.navigationLoading).toMatchObject({
      kind: 'thread',
      title: 'Loading thread',
      description: 'Refreshing transcript and review data for Thread One.',
    });

    resolveGetThread?.({
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

    await selectPromise;

    expect(useAppStore.getState().ui.navigationLoading).toBeNull();
  });

  it('clears project navigation loading once the thread list is loaded even if ancillary project requests hang', async () => {
    mockClient.listThreads.mockResolvedValue({
      project: {
        id: 'proj-1',
        name: 'ClawSharp',
        path: '/repo',
        lastOpenedAt: '2026-04-08T10:00:00.000Z',
        lastUpdatedAt: '2026-04-08T10:01:00.000Z',
        threadCount: 0,
        gitBranch: 'main',
      },
      threads: [],
    });
    mockClient.getSettings.mockImplementation(() => new Promise(() => undefined));
    mockClient.listDiagnostics.mockImplementation(() => new Promise(() => undefined));

    const { useAppStore } = await import('@/store');
    useAppStore.setState({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          activeThreadCount: 0,
          lastUpdated: '2026-04-08T10:01:00.000Z',
        },
      ],
    });

    await useAppStore.getState().selectProject('proj-1');

    const state = useAppStore.getState();
    expect(state.ui.navigationLoading).toBeNull();
    expect(state.selectedProjectId).toBe('proj-1');
    expect(state.threads).toEqual([]);
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

  it('updates global provider settings without scoping them to the selected project', async () => {
    mockClient.updateSettings.mockResolvedValue({
      settings: {
        provider: 'openai',
        model: 'gpt-4o',
        fallbackModel: null,
        permissionMode: 'Default',
        enableTelemetry: true,
        fileCheckpointingEnabled: true,
        baseUrl: 'https://api.openai.com/v1',
        transport: 'OpenAIChatCompletions',
        configPath: '/Users/test/.claude/settings.json',
        settingsIssues: [],
        credentials: {
          hasApiKey: true,
          hasAuthToken: false,
          accountId: null,
          source: 'saved',
          hasExternalCredential: false,
          externalCredentialPath: null,
        },
        hasAnyConfiguredProviderCredential: true,
      },
    });

    const { useAppStore } = await import('@/store');
    useAppStore.setState((state) => ({
      ...state,
      selectedProjectId: 'proj-1',
      threads: [
        {
          id: 'thread-1',
          projectId: 'proj-1',
          title: 'Project One',
          summary: '',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-08T10:01:00.000Z',
          provider: 'anthropic',
          model: 'claude-haiku-4-5-20251001',
          status: 'idle',
          pinned: false,
        },
        {
          id: 'thread-2',
          projectId: 'proj-2',
          title: 'Project Two',
          summary: '',
          changedFilesCount: 0,
          target: 'local',
          lastUpdated: '2026-04-08T10:01:00.000Z',
          provider: 'anthropic',
          model: 'claude-haiku-4-5-20251001',
          status: 'idle',
          pinned: false,
        },
      ],
    }));

    await useAppStore.getState().updateSettings({
      defaultProvider: 'openai',
      defaultModel: 'gpt-4o',
    });

    expect(mockClient.updateSettings).toHaveBeenCalledWith({
      projectId: null,
      provider: 'openai',
      model: 'gpt-4o',
      fallbackModel: undefined,
      enableTelemetry: undefined,
      apiKey: undefined,
      authToken: undefined,
      accountId: undefined,
      clearApiKey: undefined,
      clearAuthToken: undefined,
      clearAccountId: undefined,
      useExternalCredential: undefined,
    });
    expect(useAppStore.getState().threads.map((thread) => thread.provider)).toEqual(['openai', 'openai']);
    expect(useAppStore.getState().threads.map((thread) => thread.model)).toEqual(['gpt-4o', 'gpt-4o']);
  });

  it('does not let a stale background settings refresh overwrite a newer provider save', async () => {
    let resolveBackgroundSettings: ((value: {
      settings: {
        provider: string;
        model: string;
        fallbackModel: null;
        permissionMode: string;
        enableTelemetry: boolean;
        fileCheckpointingEnabled: boolean;
        baseUrl: string;
        transport: string;
        configPath: string;
        settingsIssues: string[];
        credentials: {
          hasApiKey: boolean;
          hasAuthToken: boolean;
          accountId: null;
          source: 'none' | 'saved' | 'external';
          hasExternalCredential: boolean;
          externalCredentialPath: null;
        };
        hasAnyConfiguredProviderCredential: boolean;
      };
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
          threadCount: 0,
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
        threadCount: 0,
        gitBranch: 'main',
      },
      threads: [],
    });
    mockClient.getSettings
      .mockResolvedValueOnce({
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
      })
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveBackgroundSettings = resolve;
          }),
      );
    mockClient.updateSettings.mockResolvedValue({
      settings: {
        provider: 'openai',
        model: 'gpt-4o',
        fallbackModel: null,
        permissionMode: 'Default',
        enableTelemetry: true,
        fileCheckpointingEnabled: true,
        baseUrl: 'https://api.openai.com/v1',
        transport: 'OpenAIChatCompletions',
        configPath: '/Users/test/.claude/settings.json',
        settingsIssues: [],
        credentials: {
          hasApiKey: true,
          hasAuthToken: false,
          accountId: null,
          source: 'saved',
          hasExternalCredential: false,
          externalCredentialPath: null,
        },
        hasAnyConfiguredProviderCredential: true,
      },
    });
    mockClient.validateProviderConfig.mockResolvedValue({
      validation: {
        provider: 'openai',
        isValid: true,
        errors: [],
        warnings: [],
      },
    });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();
    await Promise.resolve();

    await useAppStore.getState().updateSettings({
      defaultProvider: 'openai',
      defaultModel: 'gpt-4o',
    });

    resolveBackgroundSettings?.({
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
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(useAppStore.getState().settings.defaultProvider).toBe('openai');
    expect(useAppStore.getState().settings.defaultModel).toBe('gpt-4o');
  });

  it('reloads the provider catalog when settings opens after startup provider discovery failed', async () => {
    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({ projects: [] });
    mockClient.listProviders
      .mockRejectedValueOnce(new Error('provider catalog unavailable during startup'))
      .mockResolvedValueOnce({
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
          {
            id: 'openai',
            displayName: 'OpenAI',
            defaultModel: 'gpt-4o',
            models: ['gpt-4o'],
            baseUrl: 'https://api.openai.com/v1',
            requiresApiKey: true,
            description: 'OpenAI chat completions transport.',
          },
        ],
      });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();

    expect(useAppStore.getState().settings.availableProviders).toEqual([]);

    useAppStore.getState().toggleSettings();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(useAppStore.getState().settings.availableProviders.map((provider) => provider.id)).toEqual([
      'anthropic',
      'openai',
    ]);
  });

  it('persists switching codex back to saved credentials by sending useExternalCredential false', async () => {
    mockClient.updateSettings.mockResolvedValue({
      settings: {
        provider: 'codex',
        model: 'codexplan',
        fallbackModel: null,
        permissionMode: 'Default',
        enableTelemetry: true,
        fileCheckpointingEnabled: true,
        baseUrl: 'https://chatgpt.com/backend-api/codex',
        transport: 'CodexResponses',
        configPath: '/Users/test/.claude/settings.json',
        settingsIssues: [],
        credentials: {
          hasApiKey: true,
          hasAuthToken: false,
          accountId: 'acct-123',
          source: 'saved',
          hasExternalCredential: true,
          externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
        },
        hasAnyConfiguredProviderCredential: true,
      },
    });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().updateSettings({
      useExternalProviderCredential: false,
    });

    expect(mockClient.updateSettings).toHaveBeenCalledWith({
      projectId: null,
      provider: undefined,
      model: undefined,
      fallbackModel: undefined,
      enableTelemetry: undefined,
      apiKey: undefined,
      authToken: undefined,
      accountId: undefined,
      clearApiKey: undefined,
      clearAuthToken: undefined,
      clearAccountId: undefined,
      useExternalCredential: false,
    });
  });

  it('loads global settings during initialize even when no project is open', async () => {
    mockClient.connect.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });
    mockClient.listRecentProjects.mockResolvedValue({
      projects: [],
    });
    mockClient.getSettings.mockResolvedValue({
      settings: {
        provider: 'codex',
        model: 'codexplan',
        fallbackModel: null,
        permissionMode: 'Default',
        enableTelemetry: true,
        fileCheckpointingEnabled: true,
        baseUrl: 'https://chatgpt.com/backend-api/codex',
        transport: 'CodexResponses',
        configPath: '/Users/test/.claude/settings.json',
        settingsIssues: [],
        credentials: {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: 'external',
          hasExternalCredential: true,
          externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
        },
        hasAnyConfiguredProviderCredential: true,
      },
    });

    const { useAppStore } = await import('@/store');
    await useAppStore.getState().initialize();

    expect(mockClient.getSettings).toHaveBeenCalledWith(null);
    expect(useAppStore.getState().settings.defaultProvider).toBe('codex');
    expect(useAppStore.getState().settings.defaultModel).toBe('codexplan');
    expect(useAppStore.getState().settings.hasAnyConfiguredProviderCredential).toBe(true);
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
