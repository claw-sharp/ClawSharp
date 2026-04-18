import type { RuntimeClient } from '@/lib/runtimeClient';
import type {
  AgentHostApprovalRequest,
  AgentHostCommandMap,
  AgentHostDiagnostics,
  AgentHostEventEnvelope,
  AgentHostProject,
  AgentHostProviderOption,
  AgentHostStateEvent,
  AgentHostThreadDetail,
  AgentHostThreadMessage,
  AgentHostThreadSummary,
  AgentHostThreadWorktree,
  ListProvidersResponse,
} from '@/lib/protocol';

type EventHandler = (event: AgentHostEventEnvelope) => void;
type StateHandler = (event: AgentHostStateEvent) => void;

type ApiProject = {
  id: string;
  name: string;
  path: string;
  gitBranch?: string | null;
  threadCount: number;
  lastUpdatedAt: string;
};

type ApiThreadSummary = {
  id: string;
  projectId: string;
  title: string;
  summary: string;
  status: string;
  target: string;
  provider: string;
  model: string;
  messageCount: number;
  lastUpdatedAt: string;
};

type ApiThreadMessage = {
  id: string;
  threadId: string;
  role: string;
  content: string;
  timestamp: string;
};

type ApiThreadDetail = {
  thread: ApiThreadSummary;
  messages: ApiThreadMessage[];
  hasMoreMessages: boolean;
  nextBeforeMessageId?: string | null;
};

type ApiChangedFile = {
  path: string;
  status: string;
  additions: number;
  deletions: number;
  threadId: string;
};

type ApiDiff = {
  filePath: string;
  hunks: Array<{
    header: string;
    lines: Array<{
      type: string;
      content: string;
      oldLineNumber?: number | null;
      newLineNumber?: number | null;
    }>;
  }>;
};

type ApiSettings = {
  provider: string;
  model: string;
  fallbackModel?: string | null;
  permissionMode: string;
  enableTelemetry: boolean;
  baseUrl: string;
  transport: string;
};

type ApiRunEnvelope = {
  runId: string;
  threadId: string;
  kind: string;
  timestamp: string;
  message?: string | null;
  textDelta?: string | null;
  detail?: string | null;
};

const providerCatalog: AgentHostProviderOption[] = [
  {
    id: 'anthropic',
    displayName: 'Anthropic',
    defaultModel: 'claude-haiku-4-5-20251001',
    models: ['claude-haiku-4-5-20251001', 'claude-sonnet-4-5'],
    baseUrl: 'https://api.anthropic.com',
    requiresApiKey: true,
    description: 'Claude provider via remote API mode.',
  },
  {
    id: 'openai',
    displayName: 'OpenAI',
    defaultModel: 'gpt-4.1',
    models: ['gpt-4.1', 'gpt-5.4-mini'],
    baseUrl: 'https://api.openai.com/v1',
    requiresApiKey: true,
    description: 'OpenAI provider via remote API mode.',
  },
];

function ensureBaseUrl(): string {
  return import.meta.env.VITE_DESKTOP_API_BASE_URL || 'http://127.0.0.1:5055';
}

export class RemoteApiClient implements RuntimeClient {
  private readonly baseUrl: string;
  private eventHandler: EventHandler | null = null;
  private stateHandler: StateHandler | null = null;
  private eventSources = new Map<string, EventSource>();
  private threadProjects = new Map<string, string>();

  constructor(baseUrl = ensureBaseUrl()) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  async connect(): Promise<AgentHostCommandMap['health']['response']> {
    const health = await this.get<{
      service: string;
      status: string;
      supportedClientModes: string[];
    }>('/health');

    this.stateHandler?.({ status: 'connected', detail: `Remote API ${health.status}` });

    return {
      hostName: health.service,
      hostVersion: 'remote-preview',
      protocolVersion: 'remote-v1',
      currentWorkingDirectory: '/remote',
      supportedCommands: health.supportedClientModes,
    };
  }

  async openProject(projectPath: string): Promise<AgentHostCommandMap['openProject']['response']> {
    const recent = await this.listRecentProjects();
    const project = recent.projects.find((entry) => entry.path === projectPath);
    if (!project) {
      throw new Error(`Remote project '${projectPath}' is not available.`);
    }

    const threads = await this.listThreads(project.id);
    return { project, threads: threads.threads };
  }

  async listRecentProjects(): Promise<AgentHostCommandMap['listRecentProjects']['response']> {
    const projects = await this.get<ApiProject[]>('/v1/projects');
    return { projects: projects.map(mapProject) };
  }

  async listThreads(projectId: string): Promise<AgentHostCommandMap['listThreads']['response']> {
    const [projects, threads] = await Promise.all([
      this.listRecentProjects(),
      this.get<ApiThreadSummary[]>(`/v1/projects/${encodeURIComponent(projectId)}/threads`),
    ]);
    const project = projects.projects.find((entry) => entry.id === projectId);
    if (!project) {
      throw new Error(`Project '${projectId}' is not available.`);
    }

    const mappedThreads = threads.map((thread) => mapThreadSummary(thread));
    for (const thread of mappedThreads) {
      this.threadProjects.set(thread.id, thread.projectId);
    }

    return { project, threads: mappedThreads };
  }

  async createThread(projectId: string, title?: string | null): Promise<AgentHostCommandMap['createThread']['response']> {
    const [projects, detail] = await Promise.all([
      this.listRecentProjects(),
      this.post<ApiThreadDetail>('/v1/threads', { projectId, title: title ?? null }),
    ]);
    const project = projects.projects.find((entry) => entry.id === projectId);
    if (!project) {
      throw new Error(`Project '${projectId}' is not available.`);
    }

    this.threadProjects.set(detail.thread.id, projectId);

    return {
      project,
      thread: mapThreadDetail(detail),
    };
  }

  async getThread(
    projectId: string,
    threadId: string,
    options?: { beforeMessageId?: string | null; pageSize?: number | null },
  ): Promise<AgentHostCommandMap['getThread']['response']> {
    const query = new URLSearchParams();
    if (options?.beforeMessageId) {
      query.set('beforeMessageId', options.beforeMessageId);
    }
    if (typeof options?.pageSize === 'number') {
      query.set('pageSize', options.pageSize.toString());
    }

    const [projects, detail] = await Promise.all([
      this.listRecentProjects(),
      this.get<ApiThreadDetail>(
        `/v1/projects/${encodeURIComponent(projectId)}/threads/${encodeURIComponent(threadId)}${query.size > 0 ? `?${query.toString()}` : ''}`,
      ),
    ]);
    const project = projects.projects.find((entry) => entry.id === projectId);
    if (!project) {
      throw new Error(`Project '${projectId}' is not available.`);
    }

    this.threadProjects.set(threadId, projectId);
    return { project, thread: mapThreadDetail(detail) };
  }

  async renameThread(projectId: string, threadId: string, title: string): Promise<AgentHostCommandMap['renameThread']['response']> {
    const thread = await this.getThread(projectId, threadId);
    return {
      ...thread,
      thread: {
        ...thread.thread,
        thread: {
          ...thread.thread.thread,
          title,
        },
      },
    };
  }

  async archiveThread(projectId: string, threadId: string): Promise<AgentHostCommandMap['archiveThread']['response']> {
    await this.post('/v1/threads/archive', { projectId, threadId });
    return {
      projectId,
      threadId,
      archived: true,
      timestamp: new Date().toISOString(),
    };
  }

  async startRun(projectId: string, threadId: string, prompt: string): Promise<AgentHostCommandMap['startRun']['response']> {
    this.threadProjects.set(threadId, projectId);
    this.ensureThreadStream(threadId);
    const response = await this.post<{ runId: string; threadId: string; acceptedAt: string }>('/v1/runs/start', {
      projectId,
      threadId,
      prompt,
    });
    return response;
  }

  async cancelRun(runId: string): Promise<AgentHostCommandMap['cancelRun']['response']> {
    await this.post('/v1/runs/cancel', { runId });
    return {
      runId,
      cancelled: true,
      timestamp: new Date().toISOString(),
    };
  }

  async retryRun(threadId: string, projectId?: string | null, fromMessageId?: string | null): Promise<AgentHostCommandMap['retryRun']['response']> {
    const resolvedProjectId = projectId ?? this.threadProjects.get(threadId) ?? 'project-demo';
    this.threadProjects.set(threadId, resolvedProjectId);
    this.ensureThreadStream(threadId);
    return await this.post('/v1/runs/retry', { threadId, projectId: resolvedProjectId, fromMessageId: fromMessageId ?? null });
  }

  async listChangedFiles(projectId: string, threadId?: string | null): Promise<AgentHostCommandMap['listChangedFiles']['response']> {
    const query = threadId ? `?threadId=${encodeURIComponent(threadId)}` : '';
    const files = await this.get<ApiChangedFile[]>(`/v1/projects/${encodeURIComponent(projectId)}/changed-files${query}`);
    return {
      projectId,
      threadId: threadId ?? null,
      files: files.map((file) => ({
        path: file.path,
        status: file.status as 'A' | 'M' | 'D',
        additions: file.additions,
        deletions: file.deletions,
        threadId: file.threadId,
      })),
    };
  }

  async getDiff(projectId: string, filePath: string, threadId?: string | null): Promise<AgentHostCommandMap['getDiff']['response']> {
    const query = new URLSearchParams({ filePath });
    if (threadId) {
      query.set('threadId', threadId);
    }

    const diff = await this.get<ApiDiff>(`/v1/projects/${encodeURIComponent(projectId)}/diff?${query.toString()}`);
    return {
      projectId,
      threadId: threadId ?? null,
      diff,
    };
  }

  async openExternalEditor(): Promise<AgentHostCommandMap['openExternalEditor']['response']> {
    return {
      launch: {
        launched: false,
        command: 'remote-api',
        arguments: [],
        message: 'External editor integration is only available in desktop local mode.',
      },
    };
  }

  async listDiagnostics(_projectId?: string | null, threadId?: string | null): Promise<AgentHostCommandMap['listDiagnostics']['response']> {
    const diagnostics: AgentHostDiagnostics = {
      threadId: threadId ?? null,
      sessionId: threadId ?? 'remote-api',
      provider: 'anthropic',
      model: 'claude-haiku-4-5-20251001',
      baseUrl: this.baseUrl,
      transport: 'Http+Sse',
      environment: 'RemoteApi',
      configPath: '/remote/config',
      uptime: '00:05:00',
      memoryUsage: 'n/a',
      logPaths: {
        debugLogPath: '/remote/logs/debug.log',
        telemetryEventsPath: '/remote/logs/events.jsonl',
        metricsPath: '/remote/logs/metrics.jsonl',
        crashPath: '/remote/logs/crash.jsonl',
        tracePath: '/remote/logs/trace.json',
        startupProfilePath: '/remote/logs/startup-profile.json',
      },
      warnings: [],
      errors: [],
      recentEvents: [],
    };
    return { diagnostics };
  }

  async listPlugins(projectId?: string | null): Promise<AgentHostCommandMap['listPlugins']['response']> {
    return {
      projectId: projectId ?? '',
      workspaceRoot: '/remote',
      plugins: [],
    };
  }

  async listAgents(projectId?: string | null): Promise<AgentHostCommandMap['listAgents']['response']> {
    return {
      projectId: projectId ?? '',
      workspaceRoot: '/remote',
      agents: [],
    };
  }

  async listSkills(projectId?: string | null): Promise<AgentHostCommandMap['listSkills']['response']> {
    return {
      projectId: projectId ?? '',
      workspaceRoot: '/remote',
      skills: [],
    };
  }

  async createAgent(): Promise<AgentHostCommandMap['createAgent']['response']> {
    throw new Error('Agent creation is not available in remote mode yet.');
  }

  async createSkill(): Promise<AgentHostCommandMap['createSkill']['response']> {
    throw new Error('Skill creation is not available in remote mode yet.');
  }

  async proposeAgent(): Promise<AgentHostCommandMap['proposeAgent']['response']> {
    throw new Error('Agent proposal is not available in remote mode yet.');
  }

  async listWorkspaceFiles(projectId?: string | null): Promise<AgentHostCommandMap['listWorkspaceFiles']['response']> {
    return {
      projectId: projectId ?? '',
      workspaceRoot: '/remote',
      files: [],
    };
  }

  async getSettings(): Promise<AgentHostCommandMap['getSettings']['response']> {
    const settings = await this.get<ApiSettings>('/v1/settings');
    return {
      settings: {
        ...settings,
        fileCheckpointingEnabled: false,
        configPath: '/remote/config',
        settingsIssues: ['Remote API mode is active. Local desktop integrations are disabled.'],
        credentials: {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: 'external',
          hasExternalCredential: true,
          externalCredentialPath: '/remote',
        },
        hasAnyConfiguredProviderCredential: true,
      },
    };
  }

  async updateSettings(request: AgentHostCommandMap['updateSettings']['request']): Promise<AgentHostCommandMap['updateSettings']['response']> {
    const current = await this.getSettings(request.projectId);
    return current;
  }

  async installPlugin(): Promise<AgentHostCommandMap['installPlugin']['response']> {
    throw new Error('Plugin installation is not available in remote mode yet.');
  }

  async setPluginEnabled(request: AgentHostCommandMap['setPluginEnabled']['request']): Promise<AgentHostCommandMap['setPluginEnabled']['response']> {
    return await this.listPlugins(request.projectId);
  }

  async savePluginOptions(request: AgentHostCommandMap['savePluginOptions']['request']): Promise<AgentHostCommandMap['savePluginOptions']['response']> {
    return await this.listPlugins(request.projectId);
  }

  async deletePluginOptions(request: AgentHostCommandMap['deletePluginOptions']['request']): Promise<AgentHostCommandMap['deletePluginOptions']['response']> {
    return await this.listPlugins(request.projectId);
  }

  async refreshPlugins(projectId?: string | null): Promise<AgentHostCommandMap['refreshPlugins']['response']> {
    return await this.listPlugins(projectId);
  }

  async listProviders(): Promise<ListProvidersResponse> {
    return { providers: providerCatalog };
  }

  async validateProviderConfig(
    request: AgentHostCommandMap['validateProviderConfig']['request'],
  ): Promise<AgentHostCommandMap['validateProviderConfig']['response']> {
    return {
      validation: {
        provider: request.provider,
        isValid: true,
        errors: [],
        warnings: [],
      },
    };
  }

  async listPendingApprovals(threadId?: string | null): Promise<AgentHostCommandMap['listPendingApprovals']['response']> {
    const query = threadId ? `?threadId=${encodeURIComponent(threadId)}` : '';
    const approvals = await this.get<Array<{
      id: string;
      action: string;
      decision: string;
      createdAt: string;
      threadId: string;
    }>>(`/v1/approvals${query}`);
    return { approvals: approvals.map(mapApproval) };
  }

  async resolveApproval(
    approvalId: string,
    decision: 'approved' | 'always_allow' | 'rejected',
  ): Promise<AgentHostCommandMap['resolveApproval']['response']> {
    const approval = await this.post<{
      id: string;
      action: string;
      decision: string;
      createdAt: string;
      threadId: string;
    }>('/v1/approvals/resolve', { approvalId, decision: mapDecision(decision) });
    return { approval: mapApproval(approval) };
  }

  async pickProjectDirectory(): Promise<string | null> {
    throw new Error('Project picking is only available in desktop local mode.');
  }

  async pickPromptFiles(): Promise<string[]> {
    return [];
  }

  async pickPromptImages(): Promise<string[]> {
    return [];
  }

  async subscribe(
    onEvent: (event: AgentHostEventEnvelope) => void,
    onState: (event: AgentHostStateEvent) => void,
  ): Promise<() => void> {
    this.eventHandler = onEvent;
    this.stateHandler = onState;
    this.stateHandler?.({ status: 'connected', detail: `Remote API ${this.baseUrl}` });

    return () => {
      for (const source of this.eventSources.values()) {
        source.close();
      }
      this.eventSources.clear();
      this.eventHandler = null;
      this.stateHandler = null;
    };
  }

  private ensureThreadStream(threadId: string) {
    if (this.eventSources.has(threadId) || typeof EventSource === 'undefined') {
      return;
    }

    const source = new EventSource(`${this.baseUrl}/v1/threads/${encodeURIComponent(threadId)}/events`);
    source.addEventListener('run', (event) => {
      const messageEvent = event as MessageEvent<string>;
      const payload = JSON.parse(messageEvent.data) as ApiRunEnvelope;
      const projectId = this.threadProjects.get(threadId) ?? 'project-demo';
      const envelope = mapRunEvent(payload, projectId);
      if (envelope) {
        this.eventHandler?.(envelope);
      }
    });
    source.onerror = () => {
      this.stateHandler?.({ status: 'warning', detail: `Remote event stream interrupted for ${threadId}` });
    };
    this.eventSources.set(threadId, source);
  }

  private async get<TResponse>(path: string): Promise<TResponse> {
    const response = await fetch(`${this.baseUrl}${path}`);
    if (!response.ok) {
      throw new Error(`Remote request failed: ${response.status} ${response.statusText}`);
    }

    return await response.json() as TResponse;
  }

  private async post<TResponse = unknown>(path: string, body: unknown): Promise<TResponse> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(`Remote request failed: ${response.status} ${response.statusText}`);
    }

    if (response.status === 204) {
      return undefined as TResponse;
    }

    return await response.json() as TResponse;
  }
}

function mapProject(project: ApiProject): AgentHostProject {
  return {
    id: project.id,
    name: project.name,
    path: project.path,
    lastOpenedAt: project.lastUpdatedAt,
    lastUpdatedAt: project.lastUpdatedAt,
    threadCount: project.threadCount,
    gitBranch: project.gitBranch ?? null,
  };
}

function mapThreadWorktree(projectId: string): AgentHostThreadWorktree {
  return {
    repoRoot: `/remote/${projectId}`,
    worktreePath: `/remote/${projectId}`,
    worktreeStatus: 'remote',
    baseBranch: 'remote',
    baseCommit: null,
  };
}

function mapThreadSummary(thread: ApiThreadSummary): AgentHostThreadSummary {
  return {
    id: thread.id,
    projectId: thread.projectId,
    title: thread.title,
    summary: thread.summary,
    lastUpdatedAt: thread.lastUpdatedAt,
    messageCount: thread.messageCount,
    transcriptPath: `/remote/${thread.projectId}/${thread.id}.jsonl`,
    worktree: mapThreadWorktree(thread.projectId),
  };
}

function mapThreadDetail(detail: ApiThreadDetail): AgentHostThreadDetail {
  return {
    thread: mapThreadSummary(detail.thread),
    messages: detail.messages.map(mapMessage),
    hasMoreMessages: detail.hasMoreMessages,
    nextBeforeMessageId: detail.nextBeforeMessageId ?? null,
  };
}

function mapMessage(message: ApiThreadMessage): AgentHostThreadMessage {
  return {
    id: message.id,
    threadId: message.threadId,
    role: message.role,
    content: message.content,
    timestamp: message.timestamp,
  };
}

function mapApproval(approval: { id: string; action: string; decision: string; createdAt: string }): AgentHostApprovalRequest {
  return {
    id: approval.id,
    action: approval.action,
    decision: approval.decision.toLowerCase(),
    createdAt: approval.createdAt,
  };
}

function mapDecision(decision: 'approved' | 'always_allow' | 'rejected'): string {
  switch (decision) {
    case 'always_allow':
      return 'AlwaysAllow';
    case 'rejected':
      return 'Rejected';
    default:
      return 'Approved';
  }
}

function mapRunEvent(payload: ApiRunEnvelope, projectId: string): AgentHostEventEnvelope | null {
  switch (payload.kind) {
    case 'Started':
      return {
        event: 'RunStarted',
        timestamp: payload.timestamp,
        payload: {
          runId: payload.runId,
          threadId: payload.threadId,
          projectId,
          prompt: payload.message ?? '',
          timestamp: payload.timestamp,
        },
      };
    case 'TextDelta':
      return {
        event: 'RunTextDelta',
        timestamp: payload.timestamp,
        payload: {
          runId: payload.runId,
          threadId: payload.threadId,
          delta: payload.textDelta ?? '',
          timestamp: payload.timestamp,
        },
      };
    case 'MessageCompleted':
      return {
        event: 'RunMessageCompleted',
        timestamp: payload.timestamp,
        payload: {
          runId: payload.runId,
          threadId: payload.threadId,
          message: {
            id: `remote-${payload.runId}`,
            threadId: payload.threadId,
            role: 'assistant',
            content: payload.message ?? '',
            timestamp: payload.timestamp,
          },
          timestamp: payload.timestamp,
        },
      };
    case 'Completed':
      return {
        event: 'RunCompleted',
        timestamp: payload.timestamp,
        payload: {
          runId: payload.runId,
          threadId: payload.threadId,
          reason: payload.detail ?? 'completed',
          errorMessage: null,
          timestamp: payload.timestamp,
        },
      };
    case 'Failed':
      return {
        event: 'RunFailed',
        timestamp: payload.timestamp,
        payload: {
          runId: payload.runId,
          threadId: payload.threadId,
          errorMessage: payload.detail ?? 'Remote run failed',
          timestamp: payload.timestamp,
        },
      };
    default:
      return null;
  }
}
