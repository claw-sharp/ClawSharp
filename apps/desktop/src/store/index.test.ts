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
});
