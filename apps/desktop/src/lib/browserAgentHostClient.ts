import type { UnlistenFn } from '@tauri-apps/api/event';
import { mockChangedFiles, mockDiffs } from '@/mocks/diffs';
import { mockDiagnostics } from '@/mocks/diagnostics';
import { mockMessages } from '@/mocks/messages';
import { mockProjects } from '@/mocks/projects';
import { mockThreads } from '@/mocks/threads';
import type {
  AgentHostApprovalRequest,
  AgentHostDiagnostics,
  AgentHostDiff,
  AgentHostEventEnvelope,
  AgentHostProject,
  AgentHostProviderOption,
  AgentHostRuntimeSettings,
  AgentHostStateEvent,
  AgentHostThreadMessage,
  AgentHostThreadSummary,
  CancelRunResponse,
  CreateThreadResponse,
  GetDiffResponse,
  GetSettingsResponse,
  GetThreadResponse,
  HealthResponse,
  ListChangedFilesResponse,
  ListDiagnosticsResponse,
  ListPendingApprovalsResponse,
  ListProvidersResponse,
  ListRecentProjectsResponse,
  ListThreadsResponse,
  OpenExternalEditorRequest,
  OpenExternalEditorResponse,
  OpenProjectResponse,
  RenameThreadResponse,
  ResolveApprovalResponse,
  StartRunResponse,
  UpdateSettingsRequest,
  UpdateSettingsResponse,
  ValidateProviderConfigResponse,
} from '@/lib/protocol';

type EventListener = (event: AgentHostEventEnvelope) => void;
type StateListener = (event: AgentHostStateEvent) => void;

const DEFAULT_CONFIG_PATH = '~/.clawsharp/config.toml';
const DEFAULT_LOG_PATHS = {
  debugLogPath: '~/.clawsharp/logs/debug.log',
  telemetryEventsPath: '~/.clawsharp/logs/events.jsonl',
  metricsPath: '~/.clawsharp/logs/metrics.jsonl',
  crashPath: '~/.clawsharp/logs/crash.jsonl',
  tracePath: '~/.clawsharp/logs/trace.json',
  startupProfilePath: '~/.clawsharp/logs/startup-profile.json',
};

function deepClone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

function nowIso(): string {
  return new Date().toISOString();
}

function createProviderCatalog(): AgentHostProviderOption[] {
  return [
    {
      id: 'anthropic',
      displayName: 'Anthropic',
      defaultModel: 'claude-haiku-4-5-20251001',
      models: ['claude-haiku-4-5-20251001', 'claude-sonnet-4-5', 'claude-opus-4-1'],
      baseUrl: 'https://api.anthropic.com',
      requiresApiKey: true,
      description: 'Claude default provider selection.',
    },
    {
      id: 'openai',
      displayName: 'OpenAI',
      defaultModel: 'gpt-4.1',
      models: ['gpt-4.1', 'o3'],
      baseUrl: 'https://api.openai.com/v1',
      requiresApiKey: true,
      description: 'OpenAI chat completions transport.',
    },
    {
      id: 'codex',
      displayName: 'Codex',
      defaultModel: 'gpt-5.4',
      models: ['gpt-5.4', 'gpt-5.4-mini', 'codexplan'],
      baseUrl: 'https://chatgpt.com/backend-api/codex',
      requiresApiKey: true,
      description: 'OpenAI Codex responses transport.',
    },
    {
      id: 'gemini',
      displayName: 'Gemini',
      defaultModel: 'gemini-2.5-flash',
      models: ['gemini-2.5-flash', 'gemini-2.5-pro'],
      baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai',
      requiresApiKey: true,
      description: 'Gemini OpenAI-compatible transport.',
    },
    {
      id: 'github',
      displayName: 'GitHub Models',
      defaultModel: 'github:copilot',
      models: ['github:copilot', 'openai/gpt-4.1'],
      baseUrl: 'https://models.github.ai/inference',
      requiresApiKey: true,
      description: 'GitHub-hosted models.',
    },
    {
      id: 'ollama',
      displayName: 'Ollama',
      defaultModel: 'llama3.2',
      models: ['llama3.2', 'qwen2.5-coder'],
      baseUrl: 'http://localhost:11434/v1',
      requiresApiKey: false,
      description: 'Local OpenAI-compatible provider.',
    },
  ];
}

function createRuntimeSettings(providers: AgentHostProviderOption[]): AgentHostRuntimeSettings {
  const providerId = (import.meta.env.VITE_DESKTOP_PROVIDER || 'anthropic').toLowerCase();
  const provider = providers.find((entry) => entry.id === providerId) ?? providers[0];
  const requestedModel = import.meta.env.VITE_DESKTOP_MODEL || provider.defaultModel;
  const transport = provider.id === 'anthropic'
    ? 'AnthropicMessages'
    : provider.id === 'codex'
      ? 'CodexResponses'
      : 'OpenAiChatCompletions';

  return {
    provider: provider.id,
    model: requestedModel,
    fallbackModel: null,
    permissionMode: 'Default',
    enableTelemetry: true,
    fileCheckpointingEnabled: true,
    baseUrl: provider.baseUrl,
    transport,
    configPath: provider.id === 'codex' ? '%USERPROFILE%\\.codex\\auth.json' : DEFAULT_CONFIG_PATH,
    settingsIssues: [
      'Browser preview uses mock AgentHost data. Run `npm run dev:codex` to exercise the real desktop runtime.',
    ],
    credentials: {
      hasApiKey: false,
      hasAuthToken: false,
      accountId: null,
      source: 'none',
      hasExternalCredential: false,
      externalCredentialPath: null,
    },
    hasAnyConfiguredProviderCredential: false,
  };
}

function createProjects(): AgentHostProject[] {
  return mockProjects.map((project) => ({
    id: project.id,
    name: project.name,
    path: project.path,
    lastOpenedAt: project.lastUpdated,
    lastUpdatedAt: project.lastUpdated,
    threadCount: mockThreads.filter((thread) => thread.projectId === project.id).length,
    gitBranch: project.branch ?? null,
  }));
}

function createThreads(projects: AgentHostProject[]): Record<string, AgentHostThreadSummary[]> {
  const projectLookup = new Map(projects.map((project) => [project.id, project]));
  const threadsByProject: Record<string, AgentHostThreadSummary[]> = {};

  for (const thread of mockThreads) {
    const project = projectLookup.get(thread.projectId);
    if (!project) {
      continue;
    }

    const repoRoot = project.path;
    const worktreePath = thread.target === 'worktree'
      ? `${repoRoot}/.worktrees/${thread.id}`
      : repoRoot;
    const summary: AgentHostThreadSummary = {
      id: thread.id,
      projectId: thread.projectId,
      title: thread.title,
      summary: thread.summary,
      lastUpdatedAt: thread.lastUpdated,
      messageCount: mockMessages[thread.id]?.length ?? 0,
      transcriptPath: `${repoRoot}/.clawsharp/${thread.id}.jsonl`,
      worktree: {
        repoRoot,
        worktreePath,
        worktreeStatus: thread.status,
        baseBranch: project.gitBranch ?? null,
        baseCommit: null,
      },
    };

    threadsByProject[thread.projectId] = [...(threadsByProject[thread.projectId] ?? []), summary];
  }

  return threadsByProject;
}

function createMessages(): Record<string, AgentHostThreadMessage[]> {
  return Object.fromEntries(
    Object.entries(mockMessages).map(([threadId, messages]) => [
      threadId,
      messages.map((message) => ({
        id: message.id,
        threadId: message.threadId,
        role: message.role,
        content: message.content,
        timestamp: message.timestamp,
      })),
    ]),
  );
}

function createDiagnostics(settings: AgentHostRuntimeSettings): Record<string, AgentHostDiagnostics> {
  const diagnostics = Object.fromEntries(
    Object.entries(mockDiagnostics).map(([threadId, record]) => [
      threadId,
      {
        threadId: record.threadId,
        sessionId: record.sessionId,
        provider: record.provider || settings.provider,
        model: record.model || settings.model,
        baseUrl: record.baseUrl || settings.baseUrl,
        transport: record.transport || settings.transport,
        environment: record.environment,
        configPath: record.configPath || settings.configPath,
        uptime: record.uptime,
        memoryUsage: record.memoryUsage,
        logPaths: {
          debugLogPath: record.debugLogPath || DEFAULT_LOG_PATHS.debugLogPath,
          telemetryEventsPath: record.telemetryEventsPath || DEFAULT_LOG_PATHS.telemetryEventsPath,
          metricsPath: record.metricsPath || DEFAULT_LOG_PATHS.metricsPath,
          crashPath: record.crashPath || DEFAULT_LOG_PATHS.crashPath,
          tracePath: record.tracePath || DEFAULT_LOG_PATHS.tracePath,
          startupProfilePath: record.startupProfilePath || DEFAULT_LOG_PATHS.startupProfilePath,
        },
        warnings: record.warnings,
        errors: record.errors,
        recentEvents: [],
      } satisfies AgentHostDiagnostics,
    ]),
  );

  return diagnostics;
}

function createApprovals(): AgentHostApprovalRequest[] {
  return [
    {
      id: 'approval-thread-3',
      action: 'Review the diagnostics drawer refactor before merge.',
      decision: 'pending',
      createdAt: '2026-04-07T09:10:00Z',
    },
  ];
}

function basename(projectPath: string): string {
  const normalized = projectPath.replace(/[\\/]+$/, '');
  const parts = normalized.split(/[\\/]/);
  return parts[parts.length - 1] || normalized;
}

export class BrowserAgentHostClient {
  private readonly eventListeners = new Set<EventListener>();
  private readonly stateListeners = new Set<StateListener>();
  private readonly providers = createProviderCatalog();
  private settings = createRuntimeSettings(this.providers);
  private projects = createProjects();
  private threadsByProject = createThreads(this.projects);
  private messagesByThread = createMessages();
  private changedFilesByThread = deepClone(mockChangedFiles);
  private diffsByPath = Object.fromEntries(
    Object.entries(mockDiffs).map(([key, value]) => [key, deepClone(value as AgentHostDiff)]),
  ) as Record<string, AgentHostDiff>;
  private diagnosticsByThread = createDiagnostics(this.settings);
  private approvals = createApprovals();
  private requestCounter = 0;

  async connect(): Promise<HealthResponse> {
    this.emitState({
      status: 'started',
      detail: 'Browser preview connected to the mock AgentHost client.',
    });
    this.emitEvent({
      event: 'hostReady',
      timestamp: nowIso(),
      payload: {
        hostName: 'Browser Mock AgentHost',
        hostVersion: 'preview',
        protocolVersion: 'preview',
      },
    });

    return {
      hostName: 'Browser Mock AgentHost',
      hostVersion: 'preview',
      protocolVersion: 'preview',
      currentWorkingDirectory: '/',
      supportedCommands: [
        'health',
        'listRecentProjects',
        'listThreads',
        'getThread',
        'createThread',
        'startRun',
        'retryRun',
        'cancelRun',
        'listChangedFiles',
        'getDiff',
        'listDiagnostics',
        'getSettings',
        'updateSettings',
        'listProviders',
        'validateProviderConfig',
        'listPendingApprovals',
        'resolveApproval',
      ],
    };
  }

  async openProject(projectPath: string): Promise<OpenProjectResponse> {
    const project: AgentHostProject = {
      id: `proj-${++this.requestCounter}`,
      name: basename(projectPath),
      path: projectPath,
      lastOpenedAt: nowIso(),
      lastUpdatedAt: nowIso(),
      threadCount: 0,
      gitBranch: 'main',
    };

    this.projects = [project, ...this.projects.filter((entry) => entry.id !== project.id)];
    this.threadsByProject[project.id] = [];

    return {
      project: deepClone(project),
      threads: [],
    };
  }

  async listRecentProjects(): Promise<ListRecentProjectsResponse> {
    return {
      projects: deepClone(this.projects),
    };
  }

  async listThreads(projectId: string): Promise<ListThreadsResponse> {
    const project = this.requireProject(projectId);
    return {
      project: deepClone(project),
      threads: deepClone(this.threadsByProject[projectId] ?? []),
    };
  }

  async createThread(projectId: string, title?: string | null): Promise<CreateThreadResponse> {
    const project = this.requireProject(projectId);
    const threadId = `thread-browser-${++this.requestCounter}`;
    const now = nowIso();
    const thread: AgentHostThreadSummary = {
      id: threadId,
      projectId,
      title: title?.trim() || `Browser Thread ${this.requestCounter}`,
      summary: 'Thread created in browser preview mode.',
      lastUpdatedAt: now,
      messageCount: 0,
      transcriptPath: `${project.path}/.clawsharp/${threadId}.jsonl`,
      worktree: {
        repoRoot: project.path,
        worktreePath: project.path,
        worktreeStatus: 'preview',
        baseBranch: project.gitBranch ?? null,
        baseCommit: null,
      },
    };

    this.threadsByProject[projectId] = [thread, ...(this.threadsByProject[projectId] ?? [])];
    this.messagesByThread[threadId] = [];
    this.changedFilesByThread[threadId] = [];
    this.projects = this.projects.map((entry) =>
      entry.id === projectId
        ? {
            ...entry,
            threadCount: (this.threadsByProject[projectId] ?? []).length,
            lastUpdatedAt: now,
          }
        : entry);

    return {
      project: deepClone(this.requireProject(projectId)),
      thread: {
        thread: deepClone(thread),
        messages: [],
      },
    };
  }

  async getThread(
    projectId: string,
    threadId: string,
    options?: {
      beforeMessageId?: string | null;
      pageSize?: number | null;
    },
  ): Promise<GetThreadResponse> {
    const project = this.requireProject(projectId);
    const thread = this.requireThread(projectId, threadId);
    const pageSize = Math.max(1, options?.pageSize ?? 50);
    const messages = this.messagesByThread[threadId] ?? [];
    const anchorIndex = options?.beforeMessageId
      ? messages.findIndex((message) => message.id === options.beforeMessageId)
      : messages.length;
    const endIndex = anchorIndex >= 0 ? anchorIndex : messages.length;
    const startIndex = Math.max(0, endIndex - pageSize);
    const pageMessages = messages.slice(startIndex, endIndex);
    const hasMoreMessages = startIndex > 0;
    const nextBeforeMessageId = hasMoreMessages ? pageMessages[0]?.id ?? null : null;

    return {
      project: deepClone(project),
      thread: {
        thread: deepClone(thread),
        messages: deepClone(pageMessages),
        hasMoreMessages,
        nextBeforeMessageId,
      },
    };
  }

  async renameThread(projectId: string, threadId: string, title: string): Promise<RenameThreadResponse> {
    const nextThreads = (this.threadsByProject[projectId] ?? []).map((thread) =>
      thread.id === threadId
        ? {
            ...thread,
            title,
            lastUpdatedAt: nowIso(),
          }
        : thread);
    this.threadsByProject[projectId] = nextThreads;

    return {
      project: deepClone(this.requireProject(projectId)),
      thread: {
        thread: deepClone(this.requireThread(projectId, threadId)),
        messages: deepClone(this.messagesByThread[threadId] ?? []),
      },
    };
  }

  async archiveThread(projectId: string, threadId: string) {
    this.threadsByProject[projectId] = (this.threadsByProject[projectId] ?? []).filter((thread) => thread.id !== threadId);
    delete this.messagesByThread[threadId];
    delete this.changedFilesByThread[threadId];
    delete this.diagnosticsByThread[threadId];
    this.approvals = this.approvals.filter((approval) => approval.id !== threadId && approval.id !== `approval-${threadId}`);
    this.projects = this.projects.map((project) =>
      project.id === projectId
        ? {
            ...project,
            threadCount: (this.threadsByProject[projectId] ?? []).length,
            lastUpdatedAt: nowIso(),
          }
        : project);

    return {
      projectId,
      threadId,
      archived: true,
      timestamp: nowIso(),
    };
  }

  async startRun(projectId: string, threadId: string, prompt: string): Promise<StartRunResponse> {
    this.requireThread(projectId, threadId);
    const runId = `browser-run-${++this.requestCounter}`;
    const acceptedAt = nowIso();

    window.setTimeout(() => {
      this.emitEvent({
        event: 'RunStarted',
        timestamp: acceptedAt,
        payload: {
          runId,
          threadId,
          projectId,
          prompt,
          timestamp: acceptedAt,
        },
      });
    }, 0);

    window.setTimeout(() => {
      const deltaTimestamp = nowIso();
      this.emitEvent({
        event: 'RunTextDelta',
        timestamp: deltaTimestamp,
        payload: {
          runId,
          threadId,
          delta: 'Browser preview is using mock AgentHost data. ',
          timestamp: deltaTimestamp,
        },
      });
    }, 20);

    window.setTimeout(() => {
      const messageTimestamp = nowIso();
      const assistantMessage: AgentHostThreadMessage = {
        id: `assistant-${runId}`,
        threadId,
        role: 'assistant',
        content: 'Browser preview is using mock AgentHost data. Run `npm run dev:codex` to exercise the real AgentHost and Codex provider path.',
        timestamp: messageTimestamp,
      };
      this.messagesByThread[threadId] = [...(this.messagesByThread[threadId] ?? []), assistantMessage];
      this.threadsByProject[projectId] = (this.threadsByProject[projectId] ?? []).map((thread) =>
        thread.id === threadId
          ? {
              ...thread,
              messageCount: (this.messagesByThread[threadId] ?? []).length,
              lastUpdatedAt: messageTimestamp,
            }
          : thread);

      this.emitEvent({
        event: 'RunMessageCompleted',
        timestamp: messageTimestamp,
        payload: {
          runId,
          threadId,
          message: assistantMessage,
          timestamp: messageTimestamp,
        },
      });
      this.emitEvent({
        event: 'RunCompleted',
        timestamp: messageTimestamp,
        payload: {
          runId,
          threadId,
          reason: 'Completed',
          timestamp: messageTimestamp,
        },
      });
    }, 60);

    return {
      runId,
      threadId,
      acceptedAt,
    };
  }

  async cancelRun(runId: string): Promise<CancelRunResponse> {
    return {
      runId,
      cancelled: true,
      timestamp: nowIso(),
    };
  }

  async retryRun(threadId: string, projectId?: string | null): Promise<StartRunResponse> {
    const resolvedProjectId = projectId ?? this.findProjectIdByThread(threadId);
    const lastUserPrompt = [...(this.messagesByThread[threadId] ?? [])]
      .reverse()
      .find((message) => message.role === 'user')
      ?.content ?? 'Retry last prompt';
    return await this.startRun(resolvedProjectId, threadId, lastUserPrompt);
  }

  async listChangedFiles(projectId: string, threadId?: string | null): Promise<ListChangedFilesResponse> {
    const files = threadId
      ? this.changedFilesByThread[threadId] ?? []
      : Object.values(this.changedFilesByThread).flat();
    return {
      projectId,
      threadId: threadId ?? null,
      files: deepClone(files),
    };
  }

  async getDiff(projectId: string, filePath: string, threadId?: string | null): Promise<GetDiffResponse> {
    return {
      projectId,
      threadId: threadId ?? null,
      diff: deepClone(
        this.diffsByPath[filePath] ?? {
          filePath,
          hunks: [
            {
              header: '@@ -0,0 +1,3 @@',
              lines: [
                { type: 'add', content: '// Browser preview placeholder diff', newLineNumber: 1 },
                { type: 'add', content: '// Real diffs require the desktop AgentHost runtime.', newLineNumber: 2 },
              ],
            },
          ],
        },
      ),
    };
  }

  async openExternalEditor(_request: OpenExternalEditorRequest): Promise<OpenExternalEditorResponse> {
    return {
      launch: {
        launched: false,
        command: 'browser-preview',
        arguments: [],
        message: 'Opening an external editor is only available in the Tauri desktop runtime.',
      },
    };
  }

  async listDiagnostics(projectId?: string | null, threadId?: string | null): Promise<ListDiagnosticsResponse> {
    const resolvedThreadId = threadId
      ?? (projectId ? (this.threadsByProject[projectId] ?? [])[0]?.id : undefined)
      ?? Object.keys(this.diagnosticsByThread)[0];
    const diagnostics = resolvedThreadId
      ? this.diagnosticsByThread[resolvedThreadId]
      : this.createFallbackDiagnostics();
    return {
      diagnostics: deepClone(diagnostics),
    };
  }

  async getSettings(): Promise<GetSettingsResponse> {
    return {
      settings: deepClone(this.settings),
    };
  }

  async updateSettings(request: UpdateSettingsRequest): Promise<UpdateSettingsResponse> {
    const provider = request.provider ?? this.settings.provider;
    const providerOption = this.providers.find((entry) => entry.id === provider) ?? this.providers[0];
    const nextModel = request.model
      ?? (request.provider && request.provider !== this.settings.provider ? providerOption.defaultModel : this.settings.model)
      ?? providerOption.defaultModel;
    const nextCredentials = request.provider && request.provider !== this.settings.provider
      ? {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: provider === 'codex' ? 'external' : 'none',
          hasExternalCredential: provider === 'codex',
          externalCredentialPath: provider === 'codex' ? '%USERPROFILE%\\.codex\\auth.json' : null,
        }
      : { ...this.settings.credentials };

    if (request.useExternalCredential === true) {
      nextCredentials.source = 'external';
      nextCredentials.hasExternalCredential = true;
      nextCredentials.externalCredentialPath = '%USERPROFILE%\\.codex\\auth.json';
    } else if (request.useExternalCredential === false || (request.provider === 'codex' && nextCredentials.hasExternalCredential)) {
      nextCredentials.source =
        nextCredentials.hasApiKey || nextCredentials.hasAuthToken || Boolean(nextCredentials.accountId)
          ? 'saved'
          : 'external';
    }

    if (request.clearApiKey) {
      nextCredentials.hasApiKey = false;
    }

    if (request.clearAuthToken) {
      nextCredentials.hasAuthToken = false;
    }

    if (request.clearAccountId) {
      nextCredentials.accountId = null;
    }

    if (request.apiKey !== undefined && request.apiKey !== null) {
      nextCredentials.hasApiKey = request.apiKey.trim().length > 0;
      if (nextCredentials.hasApiKey && !request.useExternalCredential) {
        nextCredentials.source = 'saved';
      }
    }

    if (request.authToken !== undefined && request.authToken !== null) {
      nextCredentials.hasAuthToken = request.authToken.trim().length > 0;
      if (nextCredentials.hasAuthToken && !request.useExternalCredential) {
        nextCredentials.source = 'saved';
      }
    }

    if (request.accountId !== undefined && request.accountId !== null) {
      nextCredentials.accountId = request.accountId.trim() || null;
      if (nextCredentials.accountId && !request.useExternalCredential) {
        nextCredentials.source = 'saved';
      }
    }

    if (!nextCredentials.hasApiKey && !nextCredentials.hasAuthToken && !nextCredentials.accountId) {
      nextCredentials.source = nextCredentials.hasExternalCredential ? 'external' : 'none';
    }

    this.settings = {
      ...this.settings,
      provider,
      model: nextModel,
      fallbackModel: request.fallbackModel ?? this.settings.fallbackModel,
      enableTelemetry: request.enableTelemetry ?? this.settings.enableTelemetry,
      baseUrl: providerOption.baseUrl,
      transport: providerOption.id === 'anthropic'
        ? 'AnthropicMessages'
        : providerOption.id === 'codex'
          ? 'CodexResponses'
          : 'OpenAiChatCompletions',
      credentials: nextCredentials,
      hasAnyConfiguredProviderCredential:
        nextCredentials.hasApiKey ||
        nextCredentials.hasAuthToken ||
        Boolean(nextCredentials.accountId) ||
        nextCredentials.hasExternalCredential,
    };

    return {
      settings: deepClone(this.settings),
    };
  }

  async listProviders(): Promise<ListProvidersResponse> {
    return {
      providers: deepClone(this.providers),
    };
  }

  async validateProviderConfig(
    request: {
      projectId?: string | null;
      provider: string;
      model?: string | null;
      liveCheck?: boolean | null;
    },
  ): Promise<ValidateProviderConfigResponse> {
    const provider = request.provider;
    const model = request.model ?? null;
    const normalizedProvider = provider.trim().toLowerCase();
    const providerOption = this.providers.find((entry) => entry.id === normalizedProvider);
    const warnings = ['Browser preview uses mock AgentHost data. Real provider auth is only exercised in `tauri dev`.'];
    const errors = providerOption
      ? []
      : [`Unknown provider '${provider}'.`];
    if (providerOption && model && !providerOption.models.includes(model)) {
      warnings.push(`Model '${model}' is not in the preview catalog for ${providerOption.displayName}.`);
    }

    // Browser preview cannot prove real credentials against a live provider API.
    if (request.liveCheck) {
      warnings.push('Live provider validation is unavailable in browser preview mode.');
    }

    return {
      validation: {
        provider: normalizedProvider,
        isValid: errors.length === 0,
        errors,
        warnings,
      },
    };
  }

  async listPendingApprovals(threadId?: string | null): Promise<ListPendingApprovalsResponse> {
    const approvals = threadId
      ? this.approvals.filter((approval) => approval.id === `approval-${threadId}` || approval.id === threadId || approval.action.includes(threadId))
      : this.approvals;
    return {
      approvals: deepClone(approvals.filter((approval) => approval.decision === 'pending')),
    };
  }

  async resolveApproval(approvalId: string, decision: 'approved' | 'always_allow' | 'rejected'): Promise<ResolveApprovalResponse> {
    const next = this.approvals.find((approval) => approval.id === approvalId);
    if (!next) {
      throw new Error(`Approval '${approvalId}' was not found in browser preview mode.`);
    }

    next.decision = decision;
    return {
      approval: deepClone(next),
    };
  }

  async pickProjectDirectory(): Promise<string | null> {
    return null;
  }

  async subscribe(
    onEvent: (event: AgentHostEventEnvelope) => void,
    onState: (event: AgentHostStateEvent) => void,
  ): Promise<UnlistenFn> {
    this.eventListeners.add(onEvent);
    this.stateListeners.add(onState);

    return () => {
      this.eventListeners.delete(onEvent);
      this.stateListeners.delete(onState);
    };
  }

  private emitEvent(event: AgentHostEventEnvelope) {
    for (const listener of this.eventListeners) {
      listener(deepClone(event));
    }
  }

  private emitState(event: AgentHostStateEvent) {
    for (const listener of this.stateListeners) {
      listener(deepClone(event));
    }
  }

  private requireProject(projectId: string): AgentHostProject {
    const project = this.projects.find((entry) => entry.id === projectId);
    if (!project) {
      throw new Error(`Project '${projectId}' was not found in browser preview mode.`);
    }

    return project;
  }

  private requireThread(projectId: string, threadId: string): AgentHostThreadSummary {
    const thread = (this.threadsByProject[projectId] ?? []).find((entry) => entry.id === threadId);
    if (!thread) {
      throw new Error(`Thread '${threadId}' was not found in browser preview mode.`);
    }

    return thread;
  }

  private findProjectIdByThread(threadId: string): string {
    const project = this.projects.find((entry) =>
      (this.threadsByProject[entry.id] ?? []).some((thread) => thread.id === threadId));
    if (!project) {
      throw new Error(`Thread '${threadId}' is not attached to a browser preview project.`);
    }

    return project.id;
  }

  private createFallbackDiagnostics(): AgentHostDiagnostics {
    return {
      threadId: null,
      sessionId: 'browser-preview',
      provider: this.settings.provider,
      model: this.settings.model,
      baseUrl: this.settings.baseUrl,
      transport: this.settings.transport,
      environment: 'browser-preview',
      configPath: this.settings.configPath,
      uptime: 'preview',
      memoryUsage: 'n/a',
      logPaths: deepClone(DEFAULT_LOG_PATHS),
      warnings: ['Browser preview is using mock diagnostics.'],
      errors: [],
      recentEvents: [],
    };
  }
}
