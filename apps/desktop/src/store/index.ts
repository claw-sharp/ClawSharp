import { create } from 'zustand';
import { agentHostClient } from '@/lib/agentHostClient';
import type {
  AgentHostChangedFile,
  AgentHostDiagnostics,
  AgentHostProject,
  AgentHostApprovalRequest,
  AgentHostProviderOption,
  AgentHostRuntimeSettings,
  AgentHostEventEnvelope,
  AgentHostStateEvent,
  AgentHostThreadDetail,
  AgentHostThreadSummary,
  AgentHostThreadWorktree,
  HostReadyEvent,
  RunCompletedEvent,
  RunFailedEvent,
  RunMessageCompletedEvent,
  RunStartedEvent,
  RunTextDeltaEvent,
  RunToolProgressEvent as AgentHostRunToolProgressEvent,
  RunToolResultEvent,
  OpenExternalEditorRequest,
} from '@/lib/protocol';
import type {
  ChangedFile,
  ConnectionState,
  DiagnosticsRecord,
  DiffChunk,
  InboxItem,
  LogEntry,
  Message,
  Project,
  ProviderOption,
  RunState,
  SettingsState,
  Thread,
  ToolProgressEvent,
  UIState,
} from '@/types';

interface AppStore {
  projects: Project[];
  threads: Thread[];
  messages: Record<string, Message[]>;
  changedFiles: Record<string, ChangedFile[]>;
  diffs: Record<string, DiffChunk>;
  logs: Record<string, LogEntry[]>;
  terminalOutput: Record<string, string[]>;
  diagnostics: Record<string, DiagnosticsRecord>;
  automations: never[];
  inboxItems: InboxItem[];
  selectedProjectId: string;
  selectedThreadId: string;
  ui: UIState;
  run: RunState;
  settings: SettingsState;
  connection: ConnectionState;
  initialize: () => Promise<void>;
  openProjectPicker: () => Promise<void>;
  openProjectPath: (projectPath: string) => Promise<void>;
  selectProject: (id: string) => Promise<void>;
  selectThread: (id: string) => Promise<void>;
  createThread: (title?: string) => Promise<void>;
  sendPrompt: (threadId: string, prompt: string) => Promise<void>;
  retryThread: (threadId: string, fromMessageId?: string) => Promise<void>;
  cancelRun: () => Promise<void>;
  archiveThread: (threadId: string) => Promise<void>;
  resolveApproval: (approvalId: string, decision: 'approved' | 'rejected') => Promise<void>;
  openExternalEditor: (request: OpenExternalEditorRequest) => Promise<void>;
  toggleLeftSidebar: () => void;
  toggleBottomDrawer: () => void;
  setBottomDrawerTab: (tab: UIState['bottomDrawerTab']) => void;
  setRightPanelTab: (tab: UIState['rightPanelTab']) => void;
  setActiveView: (view: UIState['activeView']) => void;
  selectChangedFile: (path: string | null) => Promise<void>;
  toggleSettings: () => void;
  toggleCommandPalette: () => void;
  markInboxItemRead: (id: string) => void;
  updateSettings: (partial: Partial<SettingsState>) => Promise<void>;
}

const defaultSettings: SettingsState = {
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
};

const emptyRunState: RunState = {
  activeRunId: null,
  activeThreadId: null,
  isRunning: false,
  isStreaming: false,
  pendingApproval: false,
  progressLabel: '',
  changedFilesCount: 0,
  toolProgress: [],
  errorMessage: null,
};

const defaultUi: UIState = {
  leftSidebarCollapsed: false,
  rightPanelTab: 'files',
  bottomDrawerTab: 'logs',
  bottomDrawerOpen: true,
  settingsOpen: false,
  commandPaletteOpen: false,
  selectedChangedFile: null,
  selectedInboxItem: null,
  activeView: 'threads',
};

const defaultConnection: ConnectionState = {
  isConnected: false,
  isBootstrapping: false,
  lastEventAt: null,
  errorMessage: null,
  statusLabel: 'Disconnected',
};

let subscriptionsInitialized = false;

export const useAppStore = create<AppStore>((set, get) => ({
  projects: [],
  threads: [],
  messages: {},
  changedFiles: {},
  diffs: {},
  logs: {},
  terminalOutput: {},
  diagnostics: {},
  automations: [],
  inboxItems: [],
  selectedProjectId: '',
  selectedThreadId: '',
  ui: defaultUi,
  run: emptyRunState,
  settings: defaultSettings,
  connection: defaultConnection,

  initialize: async () => {
    if (get().connection.isBootstrapping || get().connection.isConnected) {
      return;
    }

    set((state) => ({
      connection: {
        ...state.connection,
        isBootstrapping: true,
        errorMessage: null,
        statusLabel: 'Connecting to AgentHost...',
      },
    }));

    try {
      if (!subscriptionsInitialized) {
        subscriptionsInitialized = true;
        await agentHostClient.subscribe(
          (event) => {
            handleAgentHostEvent(set, get, event);
          },
          (event) => {
            handleAgentHostState(set, event);
          },
        );
      }

      await agentHostClient.connect();
      const [recent, providers, settingsResponse] = await Promise.all([
        agentHostClient.listRecentProjects(),
        agentHostClient.listProviders(),
        agentHostClient.getSettings(null),
      ]);
      set((state) => ({
        connection: {
          ...state.connection,
          isConnected: true,
          isBootstrapping: false,
          statusLabel: recent.projects.length > 0 ? 'Connected' : 'Connected · no project open',
        },
        projects: recent.projects.map(mapProject),
        settings: mergeRuntimeSettings(
          state.settings,
          settingsResponse.settings,
          providers.providers.map(mapProviderOption),
        ),
      }));

      const approvalsResponse = await agentHostClient.listPendingApprovals(null);
      set((state) => ({
        inboxItems: mapApprovalInboxItems(approvalsResponse.approvals, state.selectedProjectId, state.selectedThreadId),
      }));

      if (recent.projects.length > 0) {
        await get().selectProject(recent.projects[0].id);
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          isBootstrapping: false,
          errorMessage: error instanceof Error ? error.message : 'Failed to initialize desktop runtime.',
          statusLabel: 'Connection failed',
        },
      }));
    }
  },

  openProjectPicker: async () => {
    try {
      const projectPath = await agentHostClient.pickProjectDirectory();
      if (!projectPath) {
        return;
      }

      await get().openProjectPath(projectPath);
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to open project picker.',
          statusLabel: 'Open project failed',
        },
      }));
    }
  },

  openProjectPath: async (projectPath) => {
    const response = await agentHostClient.openProject(projectPath);
    const openedProject = mapProject(response.project);
    const openedThreads = response.threads.map((thread) => mapThread(thread, get().settings.defaultProvider, get().settings.defaultModel));

    set((state) => {
      const projects = upsertProject(state.projects, openedProject);
      return {
        projects,
        threads: mergeThreads(state.threads, openedProject.id, openedThreads),
        selectedProjectId: openedProject.id,
        selectedThreadId: '',
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          isConnected: true,
          errorMessage: null,
          statusLabel: `Opened ${openedProject.name}`,
        },
      };
    });

    if (openedThreads.length > 0) {
      await get().selectThread(openedThreads[0].id);
    }
  },

  selectProject: async (id) => {
    if (!id) {
      return;
    }

    try {
      const [response, settingsResponse, diagnosticsResponse] = await Promise.all([
        agentHostClient.listThreads(id),
        agentHostClient.getSettings(id),
        agentHostClient.listDiagnostics(id, null),
      ]);
      const project = mapProject(response.project);
      const threads = response.threads.map((thread) => mapThread(thread, get().settings.defaultProvider, get().settings.defaultModel));
      const firstThread = threads[0]?.id ?? '';

      set((state) => ({
        projects: upsertProject(state.projects, project),
        threads: mergeThreads(state.threads, id, threads),
        selectedProjectId: id,
        selectedThreadId: firstThread,
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          errorMessage: null,
        },
        settings: mergeRuntimeSettings(state.settings, settingsResponse.settings, state.settings.availableProviders),
        diagnostics: diagnosticsResponse.diagnostics.threadId
          ? {
              ...state.diagnostics,
              [diagnosticsResponse.diagnostics.threadId]: mapDiagnosticsRecord(diagnosticsResponse.diagnostics),
            }
          : state.diagnostics,
      }));

      if (firstThread) {
        await get().selectThread(firstThread);
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to load project threads.',
          statusLabel: 'Project load failed',
        },
      }));
    }
  },

  selectThread: async (id) => {
    const projectId = get().selectedProjectId;
    if (!id || !projectId) {
      set((state) => ({ selectedThreadId: id, ui: { ...state.ui, selectedChangedFile: null } }));
      return;
    }

    try {
      const [response, changedFilesResponse, diagnosticsResponse, approvalsResponse] = await Promise.all([
        agentHostClient.getThread(projectId, id),
        agentHostClient.listChangedFiles(projectId, id),
        agentHostClient.listDiagnostics(projectId, id),
        agentHostClient.listPendingApprovals(id),
      ]);
      const detail = response.thread;
      const changedFiles = changedFilesResponse.files.map(mapChangedFile);
      set((state) => ({
        selectedThreadId: id,
        messages: {
          ...state.messages,
          [id]: detail.messages.map(mapMessage),
        },
        threads: upsertThread(
          state.threads,
          {
            ...mapThread(detail.thread, state.settings.defaultProvider, state.settings.defaultModel),
            changedFilesCount: changedFiles.length,
          },
        ),
        changedFiles: {
          ...state.changedFiles,
          [id]: changedFiles,
        },
        diagnostics: {
          ...state.diagnostics,
          [id]: mapDiagnosticsRecord(diagnosticsResponse.diagnostics),
        },
        inboxItems: mapApprovalInboxItems(approvalsResponse.approvals, projectId, id),
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          errorMessage: null,
        },
      }));
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to load thread.',
          statusLabel: 'Thread load failed',
        },
      }));
    }
  },

  createThread: async (title) => {
    const projectId = get().selectedProjectId;
    if (!projectId) {
      await get().openProjectPicker();
      return;
    }

    try {
      const response = await agentHostClient.createThread(projectId, title ?? null);
      const thread = mapThread(response.thread.thread, get().settings.defaultProvider, get().settings.defaultModel);

      set((state) => ({
        projects: upsertProject(state.projects, mapProject(response.project)),
        threads: upsertThread(state.threads, thread),
        selectedThreadId: thread.id,
        messages: {
          ...state.messages,
          [thread.id]: response.thread.messages.map(mapMessage),
        },
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          errorMessage: null,
          statusLabel: `Created ${thread.title}`,
        },
      }));

      await get().selectThread(thread.id);
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to create thread.',
          statusLabel: 'Create thread failed',
        },
      }));
    }
  },

  sendPrompt: async (threadId, prompt) => {
    const trimmedPrompt = prompt.trim();
    const projectId = get().selectedProjectId;
    if (!threadId || !projectId || !trimmedPrompt) {
      return;
    }

    const optimisticUserMessage: Message = {
      id: `local-user-${Date.now()}`,
      threadId,
      role: 'user',
      content: trimmedPrompt,
      timestamp: new Date().toISOString(),
    };

    set((state) => ({
      messages: {
        ...state.messages,
        [threadId]: [...(state.messages[threadId] ?? []), optimisticUserMessage],
      },
      threads: state.threads.map((thread) =>
        thread.id === threadId ? { ...thread, status: 'running' } : thread),
      run: {
        ...state.run,
        isRunning: true,
        isStreaming: true,
        activeThreadId: threadId,
        progressLabel: 'Starting run...',
        errorMessage: null,
      },
    }));

    try {
      const response = await agentHostClient.startRun(projectId, threadId, trimmedPrompt);
      set((state) => ({
        run: {
          ...state.run,
          activeRunId: response.runId,
          activeThreadId: threadId,
        },
      }));
    } catch (error) {
      set((state) => ({
        run: {
          ...emptyRunState,
          errorMessage: error instanceof Error ? error.message : 'Failed to start run.',
        },
        threads: state.threads.map((thread) =>
          thread.id === threadId ? { ...thread, status: 'failed' } : thread),
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to start run.',
          statusLabel: 'Run start failed',
        },
      }));
    }
  },

  retryThread: async (threadId, fromMessageId) => {
    const projectId = get().selectedProjectId;
    if (!threadId) {
      return;
    }

    try {
      const response = await agentHostClient.retryRun(threadId, projectId || null, fromMessageId ?? null);
      set((state) => ({
        run: {
          ...state.run,
          activeRunId: response.runId,
          activeThreadId: threadId,
          isRunning: true,
          isStreaming: true,
          progressLabel: 'Retrying last prompt...',
          errorMessage: null,
        },
        threads: state.threads.map((thread) =>
          thread.id === threadId ? { ...thread, status: 'running' } : thread),
      }));
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to retry run.',
          statusLabel: 'Retry failed',
        },
      }));
    }
  },

  cancelRun: async () => {
    const runId = get().run.activeRunId;
    if (!runId) {
      return;
    }

    try {
      await agentHostClient.cancelRun(runId);
    } catch (error) {
      set((state) => ({
        run: {
          ...state.run,
          errorMessage: error instanceof Error ? error.message : 'Failed to cancel run.',
        },
      }));
    }
  },

  archiveThread: async (threadId) => {
    const projectId = get().selectedProjectId;
    if (!threadId || !projectId) {
      return;
    }

    try {
      await agentHostClient.archiveThread(projectId, threadId);
      set((state) => ({
        threads: state.threads.filter((thread) => thread.id !== threadId),
        messages: Object.fromEntries(Object.entries(state.messages).filter(([id]) => id !== threadId)),
        changedFiles: Object.fromEntries(Object.entries(state.changedFiles).filter(([id]) => id !== threadId)),
        diagnostics: Object.fromEntries(Object.entries(state.diagnostics).filter(([id]) => id !== threadId)),
        selectedThreadId: state.selectedThreadId === threadId ? '' : state.selectedThreadId,
        connection: {
          ...state.connection,
          errorMessage: null,
          statusLabel: 'Thread archived',
        },
      }));

      const remaining = get().threads.filter((thread) => thread.projectId === projectId);
      if (!get().selectedThreadId && remaining[0]) {
        await get().selectThread(remaining[0].id);
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to archive thread.',
          statusLabel: 'Archive failed',
        },
      }));
    }
  },

  resolveApproval: async (approvalId, decision) => {
    try {
      await agentHostClient.resolveApproval(approvalId, decision);
      const selectedThreadId = get().selectedThreadId || null;
      const approvalsResponse = await agentHostClient.listPendingApprovals(selectedThreadId);
      set((state) => ({
        inboxItems: mapApprovalInboxItems(approvalsResponse.approvals, state.selectedProjectId, selectedThreadId),
      }));
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to resolve approval.',
          statusLabel: 'Approval update failed',
        },
      }));
    }
  },

  openExternalEditor: async (request) => {
    try {
      const response = await agentHostClient.openExternalEditor({
        ...request,
        editorCommand: request.editorCommand ?? get().settings.editorPath,
      });
      if (!response.launch.launched) {
        throw new Error(response.launch.message);
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to open external editor.',
          statusLabel: 'Editor launch failed',
        },
      }));
    }
  },

  toggleLeftSidebar: () => set((state) => ({
    ui: { ...state.ui, leftSidebarCollapsed: !state.ui.leftSidebarCollapsed },
  })),

  toggleBottomDrawer: () => set((state) => ({
    ui: { ...state.ui, bottomDrawerOpen: !state.ui.bottomDrawerOpen },
  })),

  setBottomDrawerTab: (tab) => set((state) => ({
    ui: { ...state.ui, bottomDrawerTab: tab, bottomDrawerOpen: true },
  })),

  setRightPanelTab: (tab) => set((state) => ({
    ui: { ...state.ui, rightPanelTab: tab },
  })),

  setActiveView: (view) => set((state) => ({
    ui: { ...state.ui, activeView: view },
  })),

  selectChangedFile: async (path) => {
    const projectId = get().selectedProjectId;
    const threadId = get().selectedThreadId;
    set((state) => ({
      ui: { ...state.ui, selectedChangedFile: path, rightPanelTab: path ? 'diff' : 'files' },
    }));

    if (!path || !projectId || !threadId) {
      return;
    }

    try {
      const response = await agentHostClient.getDiff(projectId, path, threadId);
      set((state) => ({
        diffs: {
          ...state.diffs,
          [path]: response.diff,
        },
      }));
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to load diff.',
          statusLabel: 'Diff load failed',
        },
      }));
    }
  },

  toggleSettings: () => set((state) => ({
    ui: { ...state.ui, settingsOpen: !state.ui.settingsOpen },
  })),

  toggleCommandPalette: () => set((state) => ({
    ui: { ...state.ui, commandPaletteOpen: !state.ui.commandPaletteOpen },
  })),

  markInboxItemRead: (id) => set((state) => ({
    inboxItems: state.inboxItems.map((item) => (item.id === id ? { ...item, read: true } : item)),
  })),

  updateSettings: async (partial) => {
    const projectId = get().selectedProjectId || null;

    const localOnlyUpdate: Partial<SettingsState> = {
      theme: partial.theme,
      density: partial.density,
      streamingSpeed: partial.streamingSpeed,
      compactMode: partial.compactMode,
      reducedMotion: partial.reducedMotion,
      notifications: partial.notifications,
      editorPath: partial.editorPath,
      showDiagnostics: partial.showDiagnostics,
    };

    set((state) => ({
      settings: { ...state.settings, ...localOnlyUpdate },
    }));

    const runtimePatch = {
      projectId,
      provider: partial.defaultProvider,
      model: partial.defaultModel,
      fallbackModel: partial.fallbackModel,
      enableTelemetry: partial.showDiagnostics,
    };

    if (!runtimePatch.provider && !runtimePatch.model && runtimePatch.enableTelemetry === undefined && !runtimePatch.fallbackModel) {
      return;
    }

    try {
      const [updatedSettings, validation] = await Promise.all([
        agentHostClient.updateSettings(runtimePatch),
        agentHostClient.validateProviderConfig(
          partial.defaultProvider ?? get().settings.defaultProvider,
          partial.defaultModel ?? get().settings.defaultModel,
        ),
      ]);

      set((state) => ({
        settings: {
          ...mergeRuntimeSettings(state.settings, updatedSettings.settings, state.settings.availableProviders),
          providerValidationWarnings: validation.validation.warnings,
          providerValidationErrors: validation.validation.errors,
        },
      }));
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to update runtime settings.',
          statusLabel: 'Settings update failed',
        },
      }));
    }
  },
}));

function mapProject(project: AgentHostProject): Project {
  return {
    id: project.id,
    name: project.name,
    path: project.path,
    branch: project.gitBranch ?? null,
    activeThreadCount: project.threadCount,
    lastUpdated: project.lastUpdatedAt ?? project.lastOpenedAt,
    description: project.path,
  };
}

function mapThread(
  thread: AgentHostThreadSummary,
  provider: string,
  model: string,
): Thread {
  return {
    id: thread.id,
    projectId: thread.projectId,
    title: thread.title,
    summary: thread.summary,
    status: 'idle',
    changedFilesCount: 0,
    target: mapTarget(thread.worktree),
    lastUpdated: thread.lastUpdatedAt,
    provider,
    model,
    pinned: false,
  };
}

function mapMessage(message: AgentHostThreadDetail['messages'][number]): Message {
  return {
    id: message.id,
    threadId: message.threadId,
    role: normalizeRole(message.role),
    content: message.content,
    timestamp: message.timestamp,
  };
}

function mapChangedFile(file: AgentHostChangedFile): ChangedFile {
  return {
    path: file.path,
    status: file.status,
    additions: file.additions,
    deletions: file.deletions,
    threadId: file.threadId,
  };
}

function mapDiagnosticsRecord(diagnostics: AgentHostDiagnostics): DiagnosticsRecord {
  return {
    sessionId: diagnostics.sessionId ?? diagnostics.threadId ?? 'n/a',
    runId: diagnostics.threadId ?? 'agenthost',
    configPath: diagnostics.configPath,
    provider: diagnostics.provider,
    model: diagnostics.model,
    baseUrl: diagnostics.baseUrl,
    transport: diagnostics.transport,
    environment: diagnostics.environment,
    warnings: diagnostics.warnings,
    errors: diagnostics.errors,
    uptime: diagnostics.uptime,
    memoryUsage: diagnostics.memoryUsage,
    threadId: diagnostics.threadId ?? '',
    debugLogPath: diagnostics.logPaths.debugLogPath,
    telemetryEventsPath: diagnostics.logPaths.telemetryEventsPath,
    metricsPath: diagnostics.logPaths.metricsPath,
    crashPath: diagnostics.logPaths.crashPath,
    tracePath: diagnostics.logPaths.tracePath,
    startupProfilePath: diagnostics.logPaths.startupProfilePath,
  };
}

function mapProviderOption(provider: AgentHostProviderOption): ProviderOption {
  return {
    id: provider.id,
    displayName: provider.displayName,
    defaultModel: provider.defaultModel,
    models: provider.models,
    baseUrl: provider.baseUrl,
    requiresApiKey: provider.requiresApiKey,
    description: provider.description,
  };
}

function mapApprovalInboxItems(
  approvals: AgentHostApprovalRequest[],
  projectId: string,
  threadId: string | null,
): InboxItem[] {
  return approvals.map((approval) => ({
    id: `approval-${approval.id}`,
    type: 'review',
    title: 'Approval required',
    summary: approval.action,
    timestamp: approval.createdAt,
    read: false,
    projectId,
    threadId: threadId ?? undefined,
    approvalId: approval.id,
  }));
}

function mergeRuntimeSettings(
  state: SettingsState,
  runtime: AgentHostRuntimeSettings,
  availableProviders: ProviderOption[],
): SettingsState {
  return {
    ...state,
    defaultProvider: runtime.provider,
    defaultModel: runtime.model,
    fallbackModel: runtime.fallbackModel ?? null,
    permissionMode: runtime.permissionMode,
    providerBaseUrl: runtime.baseUrl,
    providerTransport: runtime.transport,
    configPath: runtime.configPath,
    settingsIssues: runtime.settingsIssues,
    availableProviders,
    showDiagnostics: runtime.enableTelemetry,
  };
}

function normalizeRole(role: string): Message['role'] {
  switch (role) {
    case 'assistant':
    case 'system':
    case 'tool':
      return role;
    default:
      return 'user';
  }
}

function mapTarget(worktree: AgentHostThreadWorktree): Thread['target'] {
  if (worktree.worktreePath && worktree.worktreePath !== worktree.repoRoot) {
    return 'worktree';
  }

  return 'local';
}

function upsertProject(projects: Project[], nextProject: Project): Project[] {
  const remaining = projects.filter((project) => project.id !== nextProject.id);
  return [nextProject, ...remaining];
}

function mergeThreads(currentThreads: Thread[], projectId: string, nextThreads: Thread[]): Thread[] {
  return [...currentThreads.filter((thread) => thread.projectId !== projectId), ...nextThreads];
}

function upsertThread(threads: Thread[], nextThread: Thread): Thread[] {
  const remaining = threads.filter((thread) => thread.id !== nextThread.id);
  return [nextThread, ...remaining];
}

function formatStatusLabel(status: string): string {
  switch (status) {
    case 'started':
      return 'AgentHost started';
    case 'stopped':
      return 'AgentHost stopped';
    case 'stderr':
      return 'AgentHost warning';
    case 'error':
      return 'AgentHost error';
    default:
      return status;
  }
}

function handleAgentHostState(
  set: Parameters<typeof useAppStore.setState>[0],
  event: AgentHostStateEvent,
) {
  set((state) => ({
    connection: {
      ...state.connection,
      isConnected: event.status === 'started' ? true : state.connection.isConnected,
      errorMessage: event.status === 'error' ? event.detail ?? 'AgentHost error' : state.connection.errorMessage,
      statusLabel: formatStatusLabel(event.status),
    },
    terminalOutput: event.detail
      ? {
          ...state.terminalOutput,
          [state.run.activeThreadId ?? state.selectedThreadId ?? 'host']: [
            ...(state.terminalOutput[state.run.activeThreadId ?? state.selectedThreadId ?? 'host'] ?? []),
            event.detail,
          ].slice(-200),
        }
      : state.terminalOutput,
  }));
}

function handleAgentHostEvent(
  set: Parameters<typeof useAppStore.setState>[0],
  get: () => AppStore,
  event: AgentHostEventEnvelope,
) {
  if (event.event === 'hostReady') {
    const payload = event.payload as HostReadyEvent;
    set((state) => ({
      connection: {
        ...state.connection,
        isConnected: true,
        lastEventAt: event.timestamp,
        statusLabel: `${payload.hostName} ${payload.hostVersion}`,
      },
    }));
    return;
  }

  set((state) => ({
    connection: {
      ...state.connection,
      lastEventAt: event.timestamp,
    },
  }));

  switch (event.event) {
    case 'RunStarted':
      handleRunStarted(set, event.payload as RunStartedEvent);
      break;
    case 'RunTextDelta':
      handleRunTextDelta(set, event.payload as RunTextDeltaEvent);
      break;
    case 'RunToolProgress':
      handleRunToolProgress(set, event.payload as AgentHostRunToolProgressEvent);
      break;
    case 'RunToolResult':
      handleRunToolResult(set, event.payload as RunToolResultEvent);
      break;
    case 'RunMessageCompleted':
      handleRunMessageCompleted(set, event.payload as RunMessageCompletedEvent);
      break;
    case 'RunCompleted':
      void handleRunCompleted(set, get, event.payload as RunCompletedEvent);
      break;
    case 'RunFailed':
      handleRunFailed(set, event.payload as RunFailedEvent);
      break;
  }
}

function handleRunStarted(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: RunStartedEvent,
) {
  set((state) => ({
    threads: state.threads.map((thread) =>
      thread.id === payload.threadId ? { ...thread, status: 'running', lastUpdated: payload.timestamp } : thread),
    logs: appendLogEntry(state.logs, payload.threadId, {
      id: `run-started-${payload.runId}`,
      threadId: payload.threadId,
      timestamp: payload.timestamp,
      level: 'info',
      stage: 'RunStarted',
      message: payload.prompt,
    }),
    run: {
      ...state.run,
      activeRunId: payload.runId,
      activeThreadId: payload.threadId,
      isRunning: true,
      isStreaming: true,
      progressLabel: 'Running...',
      toolProgress: [],
      errorMessage: null,
    },
  }));
}

function handleRunTextDelta(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: RunTextDeltaEvent,
) {
  set((state) => {
    const messageId = `run-${payload.runId}-assistant`;
    const currentMessages = state.messages[payload.threadId] ?? [];
    const existing = currentMessages.find((message) => message.id === messageId);
    const nextMessage: Message = existing
      ? {
          ...existing,
          content: existing.content + payload.delta,
          timestamp: payload.timestamp,
          isStreaming: true,
          toolProgress: state.run.activeRunId === payload.runId ? state.run.toolProgress : existing.toolProgress,
        }
      : {
          id: messageId,
          threadId: payload.threadId,
          role: 'assistant',
          content: payload.delta,
          timestamp: payload.timestamp,
          isStreaming: true,
          toolProgress: state.run.activeRunId === payload.runId ? state.run.toolProgress : [],
        };

    return {
      messages: {
        ...state.messages,
        [payload.threadId]: upsertMessage(currentMessages, nextMessage),
      },
      logs: appendLogEntry(state.logs, payload.threadId, {
        id: `run-delta-${payload.runId}-${payload.timestamp}`,
        threadId: payload.threadId,
        timestamp: payload.timestamp,
        level: 'debug',
        stage: 'RunTextDelta',
        message: payload.delta,
      }),
      run: state.run.activeRunId === payload.runId
        ? {
            ...state.run,
            isRunning: true,
            isStreaming: true,
          }
        : state.run,
    };
  });
}

function handleRunToolProgress(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: AgentHostRunToolProgressEvent,
) {
  set((state) => {
    const progress = mergeToolProgress(
      state.run.activeRunId === payload.runId ? state.run.toolProgress : [],
      {
        id: payload.parentToolUseId ?? payload.toolUseId,
        type: mapToolProgressType(payload.stage, payload.toolName),
        label: payload.label,
        detail: payload.detail ?? undefined,
        timestamp: payload.timestamp,
        completed: false,
      },
    );

    const messageId = `run-${payload.runId}-assistant`;
    const currentMessages = state.messages[payload.threadId] ?? [];
    const existing = currentMessages.find((message) => message.id === messageId);

    return {
      messages: existing
        ? {
            ...state.messages,
            [payload.threadId]: upsertMessage(currentMessages, {
              ...existing,
              toolProgress: progress,
            }),
          }
        : state.messages,
      logs: appendLogEntry(state.logs, payload.threadId, {
        id: `run-tool-${payload.toolUseId}-${payload.timestamp}`,
        threadId: payload.threadId,
        timestamp: payload.timestamp,
        level: payload.stage === 'waiting' ? 'warn' : 'info',
        stage: payload.toolName,
        message: payload.label,
      }),
      run: state.run.activeRunId === payload.runId
        ? {
            ...state.run,
            isRunning: true,
            toolProgress: progress,
            progressLabel: payload.label,
          }
        : state.run,
    };
  });
}

function handleRunToolResult(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: RunToolResultEvent,
) {
  set((state) => {
    const progress = state.run.activeRunId === payload.runId
      ? state.run.toolProgress.map((entry) =>
          entry.id === payload.toolUseId
            ? {
                ...entry,
                completed: true,
                detail: payload.content,
              }
            : entry)
      : state.run.toolProgress;
    return {
      logs: appendLogEntry(state.logs, payload.threadId, {
        id: `run-tool-result-${payload.toolUseId}-${payload.timestamp}`,
        threadId: payload.threadId,
        timestamp: payload.timestamp,
        level: 'info',
        stage: payload.toolName,
        message: `${payload.toolName} completed`,
      }),
      run: state.run.activeRunId === payload.runId
        ? {
            ...state.run,
            toolProgress: progress,
            progressLabel: `${payload.toolName} completed`,
          }
        : state.run,
    };
  });
}

function handleRunMessageCompleted(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: RunMessageCompletedEvent,
) {
  const nextMessage = mapMessage(payload.message);
  set((state) => ({
    messages: {
      ...state.messages,
      [payload.threadId]: upsertMessage(
        state.messages[payload.threadId] ?? [],
        {
          ...nextMessage,
          isStreaming: false,
          toolProgress: nextMessage.role === 'assistant' && state.run.activeRunId === payload.runId
            ? state.run.toolProgress
            : nextMessage.toolProgress,
        },
        `run-${payload.runId}-assistant`,
      ),
    },
  }));
}

async function handleRunCompleted(
  set: Parameters<typeof useAppStore.setState>[0],
  get: () => AppStore,
  payload: RunCompletedEvent,
) {
  set((state) => ({
    threads: state.threads.map((thread) =>
      thread.id === payload.threadId
        ? {
            ...thread,
            status: payload.reason === 'Completed' ? 'completed' : 'idle',
            lastUpdated: payload.timestamp,
        }
      : thread),
    logs: appendLogEntry(state.logs, payload.threadId, {
      id: `run-complete-${payload.runId}`,
      threadId: payload.threadId,
      timestamp: payload.timestamp,
      level: payload.errorMessage ? 'warn' : 'info',
      stage: 'RunCompleted',
      message: payload.errorMessage ?? payload.reason,
    }),
    run: state.run.activeRunId === payload.runId
      ? {
          ...emptyRunState,
          errorMessage: payload.errorMessage ?? null,
        }
      : state.run,
  }));

  const selectedProjectId = get().selectedProjectId;
  if (selectedProjectId) {
    try {
      await get().selectThread(payload.threadId);
    } catch {
      // Keep streamed local state if refresh fails.
    }
  }
}

function handleRunFailed(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: RunFailedEvent,
) {
  set((state) => ({
    threads: state.threads.map((thread) =>
      thread.id === payload.threadId ? { ...thread, status: 'failed', lastUpdated: payload.timestamp } : thread),
    logs: appendLogEntry(state.logs, payload.threadId, {
      id: `run-failed-${payload.runId}`,
      threadId: payload.threadId,
      timestamp: payload.timestamp,
      level: 'error',
      stage: 'RunFailed',
      message: payload.errorMessage,
    }),
    run: state.run.activeRunId === payload.runId
      ? {
          ...emptyRunState,
          errorMessage: payload.errorMessage,
        }
      : state.run,
    connection: {
      ...state.connection,
      errorMessage: payload.errorMessage,
      statusLabel: 'Run failed',
    },
  }));
}

function upsertMessage(messages: Message[], nextMessage: Message, replaceId?: string): Message[] {
  const idToReplace = replaceId ?? nextMessage.id;
  const remaining = messages.filter((message) => message.id !== idToReplace && message.id !== nextMessage.id);
  return [...remaining, nextMessage].sort((left, right) => left.timestamp.localeCompare(right.timestamp));
}

function mergeToolProgress(entries: ToolProgressEvent[], nextEntry: ToolProgressEvent): ToolProgressEvent[] {
  const remaining = entries.filter((entry) => entry.id !== nextEntry.id);
  return [...remaining, nextEntry].sort((left, right) => left.timestamp.localeCompare(right.timestamp));
}

function appendLogEntry(
  logs: Record<string, LogEntry[]>,
  threadId: string,
  entry: LogEntry,
): Record<string, LogEntry[]> {
  const current = logs[threadId] ?? [];
  return {
    ...logs,
    [threadId]: [...current, entry].slice(-200),
  };
}

function mapToolProgressType(stage: string, toolName: string): ToolProgressEvent['type'] {
  const normalizedStage = stage.toLowerCase();
  const normalizedTool = toolName.toLowerCase();
  if (normalizedStage.includes('waiting')) return 'waiting';
  if (normalizedTool.includes('search')) return 'searching';
  if (normalizedTool.includes('test')) return 'testing';
  if (normalizedTool.includes('read')) return 'reading';
  return 'tool';
}
