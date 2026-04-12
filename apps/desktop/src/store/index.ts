import { create } from 'zustand';
import { agentHostClient } from '@/lib/agentHostClient';
import { logToDesktop } from '@/lib/desktopLogger';
import type {
  AgentHostChangedFile,
  AgentHostDiagnostics,
  AgentHostPlugin,
  AgentHostPluginOption,
  AgentHostSkill,
  AgentHostPluginValidationIssue,
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
  ListPluginsResponse,
  ListSkillsResponse,
} from '@/lib/protocol';
import type {
  ChangedFile,
  ConnectionState,
  DiagnosticsRecord,
  DiffChunk,
  InboxItem,
  LogEntry,
  Message,
  NavigationLoadingState,
  Plugin,
  Project,
  ProviderOption,
  RunState,
  SettingsState,
  Skill,
  Thread,
  ThreadHistoryState,
  ToolProgressEvent,
  UIState,
} from '@/types';

interface AppStore {
  projects: Project[];
  pluginCatalog: Plugin[];
  pluginCatalogProjectId: string | null;
  pluginCatalogLoading: boolean;
  pluginCatalogError: string | null;
  skills: Skill[];
  skillsProjectId: string | null;
  skillsLoading: boolean;
  skillsError: string | null;
  threads: Thread[];
  messages: Record<string, Message[]>;
  threadHistory: Record<string, ThreadHistoryState>;
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
  loadPlugins: (projectId?: string | null, options?: { force?: boolean }) => Promise<void>;
  loadSkills: (projectId?: string | null, options?: { force?: boolean }) => Promise<void>;
  refreshPlugins: () => Promise<void>;
  setPluginEnabled: (pluginId: string, enabled: boolean) => Promise<void>;
  savePluginOptions: (pluginId: string, values: Record<string, unknown>) => Promise<void>;
  deletePluginOptions: (pluginId: string) => Promise<void>;
  openProjectPicker: () => Promise<void>;
  openProjectPath: (projectPath: string) => Promise<void>;
  selectProject: (id: string, options?: SelectProjectOptions) => Promise<void>;
  selectThread: (id: string, options?: SelectThreadOptions) => Promise<void>;
  loadOlderThreadMessages: (threadId: string) => Promise<void>;
  createThread: (title?: string) => Promise<void>;
  sendPrompt: (threadId: string, prompt: string) => Promise<void>;
  retryThread: (threadId: string, fromMessageId?: string) => Promise<void>;
  cancelRun: () => Promise<void>;
  archiveThread: (threadId: string) => Promise<void>;
  resolveApproval: (approvalId: string, decision: 'approved' | 'always_allow' | 'rejected') => Promise<void>;
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
  updateSettings: (partial: SettingsUpdate) => Promise<void>;
  validateProviderConfig: (request?: ProviderValidationRequest) => Promise<{
    isValid: boolean;
    warnings: string[];
    errors: string[];
  }>;
}

type SettingsUpdate = Partial<SettingsState> & {
  providerApiKey?: string | null;
  providerAuthToken?: string | null;
  providerAccountId?: string | null;
  clearProviderApiKey?: boolean;
  clearProviderAuthToken?: boolean;
  clearProviderAccountId?: boolean;
  useExternalProviderCredential?: boolean;
};

type ProviderValidationRequest = {
  provider?: string;
  model?: string;
  providerApiKey?: string | null;
  providerAuthToken?: string | null;
  providerAccountId?: string | null;
  useExternalProviderCredential?: boolean;
  liveCheck?: boolean;
};

type SelectProjectOptions = {
  showLoading?: boolean;
  preloadFirstThread?: 'await' | 'background' | 'none';
};

type SelectThreadOptions = {
  showLoading?: boolean;
  loadAncillary?: boolean;
};

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
  navigationLoading: null,
};

const defaultConnection: ConnectionState = {
  isConnected: false,
  isBootstrapping: false,
  lastEventAt: null,
  errorMessage: null,
  statusLabel: 'Disconnected',
};

const defaultThreadHistoryState: ThreadHistoryState = {
  hasMoreMessages: false,
  nextBeforeMessageId: null,
  isLoadingOlder: false,
};

const threadMessagePageSize = 50;

let subscriptionsInitialized = false;
let settingsMutationVersion = 0;
let settingsMutationsInFlight = 0;

export const useAppStore = create<AppStore>((set, get) => ({
  projects: [],
  pluginCatalog: [],
  pluginCatalogProjectId: null,
  pluginCatalogLoading: false,
  pluginCatalogError: null,
  skills: [],
  skillsProjectId: null,
  skillsLoading: false,
  skillsError: null,
  threads: [],
  messages: {},
  threadHistory: {},
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

      const initialSettingsSnapshotVersion = capturePassiveSettingsSnapshotVersion();
      const [recentResult, providersResult, settingsResult] = await Promise.allSettled([
        agentHostClient.listRecentProjects(),
        agentHostClient.listProviders(),
        agentHostClient.getSettings(null),
      ]);
      const recent = recentResult.status === 'fulfilled'
        ? recentResult.value
        : { projects: [] };
      const providers = providersResult.status === 'fulfilled'
        ? providersResult.value
        : { providers: [] };
      const runtimeSettings = settingsResult.status === 'fulfilled'
        ? settingsResult.value.settings
        : null;
      const recentProjects = recent.projects.map(mapProject);
      const mappedProviders = providers.providers.map(mapProviderOption);
      set((state) => ({
        connection: {
          ...state.connection,
          isConnected: true,
          isBootstrapping: false,
          errorMessage: null,
          statusLabel: state.selectedProjectId || recentProjects.length > 0
            ? 'Connected'
            : 'Connected · no project open',
        },
        projects: mergeProjectLists(state.projects, recentProjects),
        settings: runtimeSettings && canApplyPassiveSettingsSnapshot(initialSettingsSnapshotVersion)
          ? mergeRuntimeSettings(state.settings, runtimeSettings, mappedProviders)
          : {
              ...state.settings,
              availableProviders: mappedProviders,
            },
      }));

      const approvalsResponse = await agentHostClient.listPendingApprovals(null).catch(() => null);
      if (approvalsResponse) {
        set((state) => ({
          inboxItems: mapApprovalInboxItems(approvalsResponse.approvals, state.selectedProjectId, state.selectedThreadId),
        }));
      }

      if (!get().selectedProjectId && recent.projects.length > 0) {
        const initialProjectId = recent.projects[0].id;
        set((state) => ({
          selectedProjectId: initialProjectId,
          selectedThreadId: '',
          ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        }));
        void get().selectProject(initialProjectId, {
          showLoading: false,
          preloadFirstThread: 'background',
        });
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          isBootstrapping: false,
          errorMessage: toErrorMessage(error, 'Failed to initialize desktop runtime.'),
          statusLabel: 'Connection failed',
        },
      }));
    }
  },

  loadPlugins: async (projectId, options) => {
    const resolvedProjectId = projectId ?? get().selectedProjectId;
    if (!resolvedProjectId) {
      set({
        pluginCatalog: [],
        pluginCatalogProjectId: null,
        pluginCatalogLoading: false,
        pluginCatalogError: null,
      });
      return;
    }

    if (!options?.force &&
        get().pluginCatalogProjectId === resolvedProjectId &&
        get().pluginCatalog.length > 0 &&
        !get().pluginCatalogError) {
      return;
    }

    set({
      pluginCatalogLoading: true,
      pluginCatalogError: null,
    });

    try {
      const response = await agentHostClient.listPlugins(resolvedProjectId);
      set(applyPluginCatalogResponse(response));
    } catch (error) {
      set((state) => ({
        pluginCatalogLoading: false,
        pluginCatalogError: toErrorMessage(error, 'Failed to load plugins.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to load plugins.'),
          statusLabel: 'Plugin load failed',
        },
      }));
    }
  },

  loadSkills: async (projectId, options) => {
    const resolvedProjectId = projectId ?? get().selectedProjectId;
    if (!resolvedProjectId) {
      set({
        skills: [],
        skillsProjectId: null,
        skillsLoading: false,
        skillsError: null,
      });
      return;
    }

    if (!options?.force &&
        get().skillsProjectId === resolvedProjectId &&
        get().skills.length > 0 &&
        !get().skillsError) {
      return;
    }

    set({
      skillsLoading: true,
      skillsError: null,
    });

    try {
      const response = await agentHostClient.listSkills(resolvedProjectId);
      set(applySkillCatalogResponse(response));
    } catch (error) {
      set((state) => ({
        skillsLoading: false,
        skillsError: toErrorMessage(error, 'Failed to load skills.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to load skills.'),
          statusLabel: 'Skill load failed',
        },
      }));
    }
  },

  refreshPlugins: async () => {
    const projectId = get().selectedProjectId;
    if (!projectId) {
      return;
    }

    set({
      pluginCatalogLoading: true,
      pluginCatalogError: null,
    });

    try {
      const response = await agentHostClient.refreshPlugins(projectId);
      set((state) => ({
        ...applyPluginCatalogResponse(response)(state),
        skillsLoading: true,
        skillsError: null,
      }));
      void get().loadSkills(projectId, { force: true });
    } catch (error) {
      set((state) => ({
        pluginCatalogLoading: false,
        pluginCatalogError: toErrorMessage(error, 'Failed to refresh plugins.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to refresh plugins.'),
          statusLabel: 'Plugin refresh failed',
        },
      }));
    }
  },

  setPluginEnabled: async (pluginId, enabled) => {
    const projectId = get().selectedProjectId;
    if (!projectId) {
      return;
    }

    set({
      pluginCatalogLoading: true,
      pluginCatalogError: null,
    });

    try {
      const response = await agentHostClient.setPluginEnabled({ projectId, pluginId, enabled });
      set((state) => ({
        ...applyPluginCatalogResponse(response)(state),
        connection: {
          ...state.connection,
          errorMessage: null,
          statusLabel: enabled ? `Enabled ${pluginId}` : `Disabled ${pluginId}`,
        },
      }));
      void get().loadSkills(projectId, { force: true });
    } catch (error) {
      set((state) => ({
        pluginCatalogLoading: false,
        pluginCatalogError: toErrorMessage(error, 'Failed to update plugin state.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to update plugin state.'),
          statusLabel: 'Plugin update failed',
        },
      }));
    }
  },

  savePluginOptions: async (pluginId, values) => {
    const projectId = get().selectedProjectId;
    if (!projectId) {
      return;
    }

    set({
      pluginCatalogLoading: true,
      pluginCatalogError: null,
    });

    try {
      const response = await agentHostClient.savePluginOptions({ projectId, pluginId, values });
      set((state) => ({
        ...applyPluginCatalogResponse(response)(state),
        connection: {
          ...state.connection,
          errorMessage: null,
          statusLabel: `Saved ${pluginId} options`,
        },
      }));
      void get().loadSkills(projectId, { force: true });
    } catch (error) {
      set((state) => ({
        pluginCatalogLoading: false,
        pluginCatalogError: toErrorMessage(error, 'Failed to save plugin options.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to save plugin options.'),
          statusLabel: 'Plugin settings failed',
        },
      }));
    }
  },

  deletePluginOptions: async (pluginId) => {
    const projectId = get().selectedProjectId;
    if (!projectId) {
      return;
    }

    set({
      pluginCatalogLoading: true,
      pluginCatalogError: null,
    });

    try {
      const response = await agentHostClient.deletePluginOptions({ projectId, pluginId });
      set((state) => ({
        ...applyPluginCatalogResponse(response)(state),
        connection: {
          ...state.connection,
          errorMessage: null,
          statusLabel: `Cleared ${pluginId} options`,
        },
      }));
      void get().loadSkills(projectId, { force: true });
    } catch (error) {
      set((state) => ({
        pluginCatalogLoading: false,
        pluginCatalogError: toErrorMessage(error, 'Failed to clear plugin options.'),
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to clear plugin options.'),
          statusLabel: 'Plugin settings failed',
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
    const loadingRequestId = startNavigationLoading(
      set,
      'project',
      'Opening project',
      `Loading repository data from ${projectPath}.`,
    );

    try {
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

      await get().selectProject(openedProject.id);
    } finally {
      clearNavigationLoading(set, loadingRequestId);
    }
  },

  selectProject: async (id, options) => {
    if (!id) {
      return;
    }

    const projectName = get().projects.find((project) => project.id === id)?.name ?? id;
    const showLoading = options?.showLoading ?? true;
    const preloadFirstThread = options?.preloadFirstThread ?? 'await';
    const loadingRequestId = showLoading
      ? startNavigationLoading(
          set,
          'project',
          'Loading project',
          `Fetching threads and diagnostics for ${projectName}.`,
        )
      : null;

    try {
      const settingsSnapshotVersion = capturePassiveSettingsSnapshotVersion();
      const ancillaryPromise = Promise.allSettled([
        agentHostClient.getSettings(null),
        agentHostClient.listDiagnostics(id, null),
      ]);
      const response = await agentHostClient.listThreads(id);
      const project = mapProject(response.project);
      const threads = response.threads.map((thread) => mapThread(thread, get().settings.defaultProvider, get().settings.defaultModel));
      const firstThread = threads[0]?.id ?? '';

      set((state) => ({
        projects: upsertProject(state.projects, project),
        pluginCatalog: state.selectedProjectId === id ? state.pluginCatalog : [],
        pluginCatalogProjectId: state.selectedProjectId === id ? state.pluginCatalogProjectId : null,
        pluginCatalogLoading: state.selectedProjectId === id ? state.pluginCatalogLoading : false,
        pluginCatalogError: null,
        skills: state.selectedProjectId === id ? state.skills : [],
        skillsProjectId: state.selectedProjectId === id ? state.skillsProjectId : null,
        skillsLoading: state.selectedProjectId === id ? state.skillsLoading : false,
        skillsError: null,
        threads: mergeThreads(state.threads, id, threads),
        selectedProjectId: id,
        selectedThreadId: firstThread,
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          errorMessage: null,
        },
      }));

      void get().loadSkills(id);

      void ancillaryPromise.then(([settingsResponse, diagnosticsResponse]) => {
        if (settingsResponse.status !== 'fulfilled') {
          void logToDesktop('warn', '[desktop:project:settings-load-failed]', {
            projectId: id,
            error: toErrorMessage(settingsResponse.reason, 'Failed to load settings.'),
          });
        }

        if (diagnosticsResponse.status !== 'fulfilled') {
          void logToDesktop('warn', '[desktop:project:diagnostics-load-failed]', {
            projectId: id,
            error: toErrorMessage(diagnosticsResponse.reason, 'Failed to load project diagnostics.'),
          });
        }

        set((state) => ({
          settings: settingsResponse.status === 'fulfilled' && canApplyPassiveSettingsSnapshot(settingsSnapshotVersion)
            ? mergeRuntimeSettings(state.settings, settingsResponse.value.settings, state.settings.availableProviders)
            : state.settings,
          diagnostics: diagnosticsResponse.status === 'fulfilled' && diagnosticsResponse.value.diagnostics.threadId
            ? {
                ...state.diagnostics,
                [diagnosticsResponse.value.diagnostics.threadId]: mapDiagnosticsRecord(diagnosticsResponse.value.diagnostics),
              }
            : state.diagnostics,
        }));
      });

      if (firstThread) {
        if (preloadFirstThread === 'await') {
          await get().selectThread(firstThread, { showLoading });
        } else if (preloadFirstThread === 'background') {
          void get().selectThread(firstThread, { showLoading: false });
        }
      }
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: error instanceof Error ? error.message : 'Failed to load project threads.',
          statusLabel: 'Project load failed',
        },
      }));
    } finally {
      if (loadingRequestId) {
        clearNavigationLoading(set, loadingRequestId);
      }
    }
  },

  selectThread: async (id, options) => {
    const projectId = get().selectedProjectId;
    if (!id || !projectId) {
      set((state) => ({ selectedThreadId: id, ui: { ...state.ui, selectedChangedFile: null } }));
      return;
    }

    const threadTitle = get().threads.find((thread) => thread.id === id)?.title ?? id;
    const showLoading = options?.showLoading ?? true;
    const loadAncillary = options?.loadAncillary ?? true;
    const loadingRequestId = showLoading
      ? startNavigationLoading(
          set,
          'thread',
          'Loading thread',
          `Refreshing transcript and review data for ${threadTitle}.`,
        )
      : null;

    try {
      const ancillaryPromise = loadAncillary
        ? Promise.allSettled([
            agentHostClient.listChangedFiles(projectId, id),
            agentHostClient.listDiagnostics(projectId, id),
            agentHostClient.listPendingApprovals(id),
          ])
        : null;
      const response = await agentHostClient.getThread(projectId, id, { pageSize: threadMessagePageSize });

      const detail = response.thread;
      const existingThread = get().threads.find((thread) => thread.id === id);

      set((state) => ({
        selectedThreadId: id,
        messages: {
          ...state.messages,
          [id]: mergeHydratedMessages(state.messages[id] ?? [], detail.messages.map(mapMessage)),
        },
        threadHistory: {
          ...state.threadHistory,
          [id]: {
            hasMoreMessages: detail.hasMoreMessages ?? false,
            nextBeforeMessageId: detail.nextBeforeMessageId ?? null,
            isLoadingOlder: false,
          },
        },
        threads: upsertThread(
          state.threads,
          {
            ...mapThread(detail.thread, state.settings.defaultProvider, state.settings.defaultModel),
            changedFilesCount: existingThread?.changedFilesCount ?? 0,
          },
        ),
        ui: { ...state.ui, activeView: 'threads', selectedChangedFile: null },
        connection: {
          ...state.connection,
          errorMessage: null,
        },
      }));

      void ancillaryPromise?.then(([changedFilesResponse, diagnosticsResponse, approvalsResponse]) => {
        const changedFiles = changedFilesResponse.status === 'fulfilled'
          ? changedFilesResponse.value.files.map(mapChangedFile)
          : [];
        const diagnostics = diagnosticsResponse.status === 'fulfilled'
          ? mapDiagnosticsRecord(diagnosticsResponse.value.diagnostics)
          : null;
        const inboxItems = approvalsResponse.status === 'fulfilled'
          ? mapApprovalInboxItems(approvalsResponse.value.approvals, projectId, id)
          : null;

        if (changedFilesResponse.status !== 'fulfilled') {
          void logToDesktop('warn', '[desktop:thread:changed-files-load-failed]', {
            projectId,
            threadId: id,
            error: toErrorMessage(changedFilesResponse.reason, 'Failed to load changed files.'),
          });
        }

        if (diagnosticsResponse.status !== 'fulfilled') {
          void logToDesktop('warn', '[desktop:thread:diagnostics-load-failed]', {
            projectId,
            threadId: id,
            error: toErrorMessage(diagnosticsResponse.reason, 'Failed to load diagnostics.'),
          });
        }

        if (approvalsResponse.status !== 'fulfilled') {
          void logToDesktop('warn', '[desktop:thread:approvals-load-failed]', {
            projectId,
            threadId: id,
            error: toErrorMessage(approvalsResponse.reason, 'Failed to load approvals.'),
          });
        }

        set((state) => ({
          threads: upsertThread(
            state.threads,
            {
              ...(state.threads.find((thread) => thread.id === id)
                ?? mapThread(detail.thread, state.settings.defaultProvider, state.settings.defaultModel)),
              changedFilesCount: changedFiles.length,
            },
          ),
          changedFiles: {
            ...state.changedFiles,
            [id]: changedFiles,
          },
          diagnostics: diagnostics
            ? {
                ...state.diagnostics,
                [id]: diagnostics,
              }
            : state.diagnostics,
          inboxItems: state.selectedThreadId === id && inboxItems
            ? inboxItems
            : state.inboxItems,
        }));
      });
    } catch (error) {
      set((state) => ({
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to load thread.'),
          statusLabel: 'Thread load failed',
        },
      }));
    } finally {
      if (loadingRequestId) {
        clearNavigationLoading(set, loadingRequestId);
      }
    }
  },

  loadOlderThreadMessages: async (threadId) => {
    const projectId = get().selectedProjectId;
    const history = get().threadHistory[threadId];
    if (!projectId || !threadId || !history?.hasMoreMessages || !history.nextBeforeMessageId || history.isLoadingOlder) {
      return;
    }

    set((state) => ({
      threadHistory: {
        ...state.threadHistory,
        [threadId]: {
          ...(state.threadHistory[threadId] ?? defaultThreadHistoryState),
          isLoadingOlder: true,
        },
      },
    }));

    try {
      const response = await agentHostClient.getThread(projectId, threadId, {
        beforeMessageId: history.nextBeforeMessageId,
        pageSize: threadMessagePageSize,
      });
      const detail = response.thread;
      const olderMessages = detail.messages.map(mapMessage);

      set((state) => ({
        messages: {
          ...state.messages,
          [threadId]: mergeOlderMessages(state.messages[threadId] ?? [], olderMessages),
        },
        threadHistory: {
          ...state.threadHistory,
          [threadId]: {
            hasMoreMessages: detail.hasMoreMessages ?? false,
            nextBeforeMessageId: detail.nextBeforeMessageId ?? null,
            isLoadingOlder: false,
          },
        },
      }));
    } catch (error) {
      set((state) => ({
        threadHistory: {
          ...state.threadHistory,
          [threadId]: {
            ...(state.threadHistory[threadId] ?? defaultThreadHistoryState),
            isLoadingOlder: false,
          },
        },
        connection: {
          ...state.connection,
          errorMessage: toErrorMessage(error, 'Failed to load older thread messages.'),
          statusLabel: 'Thread pagination failed',
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
    const promptLogContext = createPromptLogContext(trimmedPrompt);

    void logToDesktop('info', '[desktop:chat:send-requested]', {
      projectId,
      threadId,
      messageId: optimisticUserMessage.id,
      ...promptLogContext,
    });

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

    void logToDesktop('debug', '[desktop:chat:send-optimistic-message]', {
      projectId,
      threadId,
      messageId: optimisticUserMessage.id,
      messageCount: (get().messages[threadId] ?? []).length,
    });

    try {
      const response = await agentHostClient.startRun(projectId, threadId, trimmedPrompt);
      void logToDesktop('info', '[desktop:chat:send-accepted]', {
        projectId,
        threadId,
        messageId: optimisticUserMessage.id,
        runId: response.runId,
        acceptedAt: response.acceptedAt,
      });
      set((state) => ({
        run: {
          ...state.run,
          activeRunId: response.runId,
          activeThreadId: threadId,
        },
      }));
    } catch (error) {
      void logToDesktop('error', '[desktop:chat:send-failed]', {
        projectId,
        threadId,
        messageId: optimisticUserMessage.id,
        ...promptLogContext,
        error: toErrorMessage(error, 'Failed to start run.'),
      });
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
      const existingItem = get().inboxItems.find((item) => item.approvalId === approvalId);
      const approvalThreadId = existingItem?.threadId ?? get().selectedThreadId ?? null;
      await agentHostClient.resolveApproval(approvalId, decision);
      const approvalsResponse = await agentHostClient.listPendingApprovals(approvalThreadId);
      const hasPendingApprovals = approvalsResponse.approvals.length > 0;
      set((state) => ({
        inboxItems: mapApprovalInboxItems(approvalsResponse.approvals, state.selectedProjectId, approvalThreadId),
        threads: approvalThreadId
          ? state.threads.map((thread) =>
              thread.id === approvalThreadId && thread.status === 'waiting_approval'
                ? {
                    ...thread,
                    status: hasPendingApprovals
                      ? 'waiting_approval'
                      : state.run.activeThreadId === approvalThreadId && state.run.isRunning
                        ? 'running'
                        : 'idle',
                  }
                : thread)
          : state.threads,
        run: approvalThreadId && state.run.activeThreadId === approvalThreadId
          ? {
              ...state.run,
              pendingApproval: hasPendingApprovals,
              progressLabel: hasPendingApprovals
                ? 'Waiting for approval...'
                : decision === 'approved'
                  ? 'Approval submitted...'
                  : 'Approval rejected...',
            }
          : state.run,
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

  setActiveView: (view) => {
    set((state) => ({
      ui: { ...state.ui, activeView: view },
    }));

    if (view === 'plugins') {
      void get().loadPlugins(get().selectedProjectId);
      void get().loadSkills(get().selectedProjectId);
    }
  },

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

  toggleSettings: () => {
    const opening = !get().ui.settingsOpen;
    set((state) => ({
      ui: { ...state.ui, settingsOpen: opening },
    }));

    if (opening) {
      void refreshSettingsSnapshot(get, set);
    }
  },

  toggleCommandPalette: () => set((state) => ({
    ui: { ...state.ui, commandPaletteOpen: !state.ui.commandPaletteOpen },
  })),

  markInboxItemRead: (id) => set((state) => ({
    inboxItems: state.inboxItems.map((item) => (item.id === id ? { ...item, read: true } : item)),
  })),

  updateSettings: async (partial) => {
    const projectId = null;

    const localOnlyUpdate = pickDefinedSettings({
      theme: partial.theme,
      density: partial.density,
      streamingSpeed: partial.streamingSpeed,
      compactMode: partial.compactMode,
      reducedMotion: partial.reducedMotion,
      notifications: partial.notifications,
      editorPath: partial.editorPath,
      showDiagnostics: partial.showDiagnostics,
    });

    set((state) => ({
      settings: { ...state.settings, ...localOnlyUpdate },
    }));

    const runtimePatch = {
      projectId,
      provider: partial.defaultProvider,
      model: partial.defaultModel,
      fallbackModel: partial.fallbackModel,
      enableTelemetry: partial.showDiagnostics,
      apiKey: partial.providerApiKey,
      authToken: partial.providerAuthToken,
      accountId: partial.providerAccountId,
      clearApiKey: partial.clearProviderApiKey,
      clearAuthToken: partial.clearProviderAuthToken,
      clearAccountId: partial.clearProviderAccountId,
      useExternalCredential: partial.useExternalProviderCredential,
    };

    if (!runtimePatch.provider &&
        !runtimePatch.model &&
        runtimePatch.enableTelemetry === undefined &&
        !runtimePatch.fallbackModel &&
        runtimePatch.apiKey === undefined &&
        runtimePatch.authToken === undefined &&
        runtimePatch.accountId === undefined &&
        !runtimePatch.clearApiKey &&
        !runtimePatch.clearAuthToken &&
        !runtimePatch.clearAccountId &&
        runtimePatch.useExternalCredential === undefined) {
      return;
    }

    beginSettingsMutation();
    try {
      const [updatedSettings, validation] = await Promise.all([
        agentHostClient.updateSettings(runtimePatch),
        agentHostClient.validateProviderConfig({
          projectId,
          provider: partial.defaultProvider ?? get().settings.defaultProvider,
          model: partial.defaultModel ?? get().settings.defaultModel,
        }),
      ]);

      set((state) => ({
        threads: state.threads.map((thread) => ({
          ...thread,
          provider: updatedSettings.settings.provider,
          model: updatedSettings.settings.model,
        })),
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
    } finally {
      endSettingsMutation();
    }
  },

  validateProviderConfig: async (request = {}) => {
    const projectId = null;
    const provider = request.provider ?? get().settings.defaultProvider;
    const model = request.model ?? get().settings.defaultModel;
    const response = await agentHostClient.validateProviderConfig({
      projectId,
      provider,
      model,
      liveCheck: request.liveCheck,
      apiKey: request.providerApiKey,
      authToken: request.providerAuthToken,
      accountId: request.providerAccountId,
      useExternalCredential: request.useExternalProviderCredential,
    });

    set((state) => ({
      settings: {
        ...state.settings,
        providerValidationWarnings: response.validation.warnings,
        providerValidationErrors: response.validation.errors,
      },
    }));

    return {
      isValid: response.validation.isValid,
      warnings: response.validation.warnings,
      errors: response.validation.errors,
    };
  },
}));

async function refreshSettingsSnapshot(
  get: () => AppStore,
  set: Parameters<typeof useAppStore.setState>[0],
): Promise<void> {
  const projectId = null;
  const settingsSnapshotVersion = capturePassiveSettingsSnapshotVersion();

  try {
    const [settingsResult, providersResult] = await Promise.allSettled([
      agentHostClient.getSettings(projectId),
      agentHostClient.listProviders(),
    ]);
    if (settingsResult.status !== 'fulfilled') {
      throw settingsResult.reason;
    }

    const settingsResponse = settingsResult.value;
    const availableProviders = providersResult.status === 'fulfilled'
      ? providersResult.value.providers.map(mapProviderOption)
      : get().settings.availableProviders;
    const validation = await agentHostClient.validateProviderConfig({
      projectId,
      provider: settingsResponse.settings.provider,
      model: settingsResponse.settings.model,
    });

    set((state) => ({
      settings: {
        ...(canApplyPassiveSettingsSnapshot(settingsSnapshotVersion)
          ? mergeRuntimeSettings(state.settings, settingsResponse.settings, availableProviders)
          : state.settings),
        providerValidationWarnings: canApplyPassiveSettingsSnapshot(settingsSnapshotVersion)
          ? validation.validation.warnings
          : state.settings.providerValidationWarnings,
        providerValidationErrors: canApplyPassiveSettingsSnapshot(settingsSnapshotVersion)
          ? validation.validation.errors
          : state.settings.providerValidationErrors,
      },
    }));
  } catch (error) {
    set((state) => ({
      connection: {
        ...state.connection,
        errorMessage: toErrorMessage(error, 'Failed to refresh runtime settings.'),
        statusLabel: 'Settings refresh failed',
      },
    }));
  }
}

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

function mapPlugin(plugin: AgentHostPlugin): Plugin {
  return {
    pluginId: plugin.pluginId,
    name: plugin.name,
    description: plugin.description ?? null,
    version: plugin.version ?? null,
    enabled: plugin.enabled,
    isBundled: plugin.isBundled,
    installPath: plugin.installPath,
    scope: plugin.scope,
    installedAt: plugin.installedAt ?? null,
    lastUpdated: plugin.lastUpdated ?? null,
    gitCommitSha: plugin.gitCommitSha ?? null,
    commands: plugin.commands,
    agents: plugin.agents,
    skills: plugin.skills,
    outputStyles: plugin.outputStyles,
    hookFiles: plugin.hookFiles,
    hookEvents: plugin.hookEvents,
    validationIssues: plugin.validationIssues.map(mapPluginValidationIssue),
    options: plugin.options.map(mapPluginOption),
  };
}

function mapPluginOption(option: AgentHostPluginOption): Plugin['options'][number] {
  return {
    key: option.key,
    type: option.type,
    title: option.title,
    description: option.description,
    required: option.required,
    multiple: option.multiple,
    sensitive: option.sensitive,
    hasValue: option.hasValue,
    value: option.value,
    defaultValue: option.defaultValue,
    min: option.min ?? null,
    max: option.max ?? null,
  };
}

function mapPluginValidationIssue(issue: AgentHostPluginValidationIssue): Plugin['validationIssues'][number] {
  return {
    path: issue.path,
    message: issue.message,
    isWarning: issue.isWarning,
  };
}

function mapSkill(skill: AgentHostSkill): Skill {
  return {
    name: skill.name,
    source: skill.source,
    filePath: skill.filePath,
    baseDirectory: skill.baseDirectory,
  };
}

function applyPluginCatalogResponse(response: ListPluginsResponse) {
  return (state: AppStore) => ({
    pluginCatalog: response.plugins.map(mapPlugin),
    pluginCatalogProjectId: response.projectId,
    pluginCatalogLoading: false,
    pluginCatalogError: null,
    connection: {
      ...state.connection,
      errorMessage: null,
      statusLabel: `Loaded ${response.plugins.length} plugin${response.plugins.length === 1 ? '' : 's'}`,
    },
  });
}

function applySkillCatalogResponse(response: ListSkillsResponse) {
  return (state: AppStore) => ({
    skills: response.skills.map(mapSkill),
    skillsProjectId: response.projectId,
    skillsLoading: false,
    skillsError: null,
    connection: {
      ...state.connection,
      errorMessage: null,
      statusLabel: `Loaded ${response.skills.length} skill${response.skills.length === 1 ? '' : 's'}`,
    },
  });
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

function startNavigationLoading(
  set: Parameters<typeof useAppStore.setState>[0],
  kind: NavigationLoadingState['kind'],
  title: string,
  description: string,
): string {
  const requestId = `nav-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  set((state) => ({
    ui: {
      ...state.ui,
      navigationLoading: {
        requestId,
        kind,
        title,
        description,
      },
    },
  }));
  return requestId;
}

function clearNavigationLoading(
  set: Parameters<typeof useAppStore.setState>[0],
  requestId: string,
) {
  set((state) => (
    state.ui.navigationLoading?.requestId !== requestId
      ? state
      : {
          ui: {
            ...state.ui,
            navigationLoading: null,
          },
        }
  ));
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
    providerCredentials: runtime.credentials,
    hasAnyConfiguredProviderCredential: runtime.hasAnyConfiguredProviderCredential ?? false,
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

function createPromptLogContext(prompt: string): {
  promptLength: number;
  promptLines: number;
  promptPreview: string;
} {
  const compactPrompt = prompt.replace(/\s+/g, ' ').trim();
  return {
    promptLength: prompt.length,
    promptLines: prompt.split(/\r?\n/).length,
    promptPreview: compactPrompt.length <= 160 ? compactPrompt : `${compactPrompt.slice(0, 157)}...`,
  };
}

function upsertProject(projects: Project[], nextProject: Project): Project[] {
  const existingIndex = projects.findIndex((project) => project.id === nextProject.id);
  if (existingIndex === -1) {
    return [nextProject, ...projects];
  }

  return projects.map((project, index) => (index === existingIndex ? nextProject : project));
}

function mergeProjectLists(currentProjects: Project[], incomingProjects: Project[]): Project[] {
  const incomingById = new Map(incomingProjects.map((project) => [project.id, project]));
  const mergedProjects = currentProjects.map((project) => incomingById.get(project.id) ?? project);

  for (const project of incomingProjects) {
    if (!mergedProjects.some((existing) => existing.id === project.id)) {
      mergedProjects.push(project);
    }
  }

  return mergedProjects;
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
          errorMessage: event.status === 'error'
            ? event.detail ?? 'AgentHost error'
            : event.status === 'started'
              ? null
              : state.connection.errorMessage,
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
        errorMessage: null,
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
    case 'ApprovalRequested':
      handleApprovalRequested(set, event.payload as AgentHostApprovalRequest);
      break;
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
      threads: state.threads.map((thread) =>
        thread.id === payload.threadId ? { ...thread, status: 'running', lastUpdated: payload.timestamp } : thread),
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
            pendingApproval: false,
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
        toolName: payload.toolName,
        label: payload.label,
        detail: payload.detail ?? undefined,
        timestamp: payload.timestamp,
        completed: false,
        status: 'running',
      },
    );

    const messageId = `run-${payload.runId}-assistant`;
    const currentMessages = state.messages[payload.threadId] ?? [];
    const existing = currentMessages.find((message) => message.id === messageId);
    const assistantMessage: Message = existing
      ? {
          ...existing,
          timestamp: payload.timestamp,
          toolProgress: progress,
        }
      : createToolActivityMessage(payload.threadId, payload.runId, payload.timestamp, progress);

    return {
      threads: state.threads.map((thread) =>
        thread.id === payload.threadId ? { ...thread, status: 'running', lastUpdated: payload.timestamp } : thread),
      messages: {
        ...state.messages,
        [payload.threadId]: upsertMessage(currentMessages, assistantMessage),
      },
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
            pendingApproval: false,
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
    const currentProgress = state.run.activeRunId === payload.runId ? state.run.toolProgress : [];
    const updatedEntry: ToolProgressEvent = {
      id: payload.toolUseId,
      type: mapToolProgressType('tool', payload.toolName),
      toolName: payload.toolName,
      label: payload.success ? `${payload.toolName} completed` : `${payload.toolName} failed`,
      detail: payload.content,
      timestamp: payload.timestamp,
      completed: payload.success,
      status: payload.success ? 'completed' : 'failed',
    };
    const progress = mergeToolProgress(currentProgress, updatedEntry);
    const messageId = `run-${payload.runId}-assistant`;
    const currentMessages = state.messages[payload.threadId] ?? [];
    const existing = currentMessages.find((message) => message.id === messageId);
    const assistantMessage: Message = existing
      ? {
          ...existing,
          timestamp: payload.timestamp,
          toolProgress: progress,
        }
      : createToolActivityMessage(payload.threadId, payload.runId, payload.timestamp, progress);
    return {
      messages: {
        ...state.messages,
        [payload.threadId]: upsertMessage(currentMessages, assistantMessage),
      },
      logs: appendLogEntry(state.logs, payload.threadId, {
        id: `run-tool-result-${payload.toolUseId}-${payload.timestamp}`,
        threadId: payload.threadId,
        timestamp: payload.timestamp,
        level: payload.success ? 'info' : 'warn',
        stage: payload.toolName,
        message: payload.success ? `${payload.toolName} completed` : `${payload.toolName} failed`,
      }),
      run: state.run.activeRunId === payload.runId
        ? {
            ...state.run,
            toolProgress: progress,
            progressLabel: payload.success ? `${payload.toolName} completed` : state.run.progressLabel,
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
  set((state) => {
    const currentMessages = state.messages[payload.threadId] ?? [];
    const existing = currentMessages.find((message) =>
      message.id === `run-${payload.runId}-assistant` || message.id === nextMessage.id);
    const mergedToolProgress = nextMessage.role === 'assistant'
      ? mergeToolProgressCollections(
          existing?.toolProgress ?? [],
          state.run.activeRunId === payload.runId ? state.run.toolProgress : [],
          nextMessage.toolProgress ?? [],
        )
      : nextMessage.toolProgress;

    return {
      messages: {
        ...state.messages,
        [payload.threadId]: upsertMessage(
          currentMessages,
          {
            ...nextMessage,
            isStreaming: false,
            toolProgress: mergedToolProgress,
          },
          `run-${payload.runId}-assistant`,
        ),
      },
    };
  });
}

async function handleRunCompleted(
  set: Parameters<typeof useAppStore.setState>[0],
  get: () => AppStore,
  payload: RunCompletedEvent,
) {
  const runSnapshot = get().run;
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
      const shouldLoadAncillary = Boolean(payload.errorMessage) ||
        runSnapshot.pendingApproval ||
        runSnapshot.toolProgress.length > 0;
      await get().selectThread(payload.threadId, {
        showLoading: false,
        loadAncillary: shouldLoadAncillary,
      });
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

function handleApprovalRequested(
  set: Parameters<typeof useAppStore.setState>[0],
  payload: AgentHostApprovalRequest,
) {
  set((state) => {
    const projectId = state.selectedProjectId;
    const threadId = state.run.activeThreadId || state.selectedThreadId || undefined;
    const nextInboxItem = {
      id: `approval-${payload.id}`,
      type: 'review' as const,
      title: 'Approval required',
      summary: payload.action,
      timestamp: payload.createdAt,
      read: false,
      projectId,
      threadId,
      approvalId: payload.id,
    };

    return {
      inboxItems: state.inboxItems.some((item) => item.id === nextInboxItem.id)
        ? state.inboxItems
        : [nextInboxItem, ...state.inboxItems],
      threads: threadId
        ? state.threads.map((thread) =>
            thread.id === threadId ? { ...thread, status: 'waiting_approval', lastUpdated: payload.createdAt } : thread)
        : state.threads,
      run: {
        ...state.run,
        isRunning: true,
        isStreaming: true,
        pendingApproval: true,
        progressLabel: 'Waiting for approval...',
      },
      logs: threadId
        ? appendLogEntry(state.logs, threadId, {
            id: `approval-${payload.id}`,
            threadId,
            timestamp: payload.createdAt,
            level: 'warn',
            stage: 'ApprovalRequested',
            message: payload.action,
          })
        : state.logs,
    };
  });
}

function upsertMessage(messages: Message[], nextMessage: Message, replaceId?: string): Message[] {
  const idToReplace = replaceId ?? nextMessage.id;
  const remaining = messages.filter((message) => message.id !== idToReplace && message.id !== nextMessage.id);
  return [...remaining, nextMessage].sort((left, right) => left.timestamp.localeCompare(right.timestamp));
}

function createToolActivityMessage(
  threadId: string,
  runId: string,
  timestamp: string,
  toolProgress: ToolProgressEvent[],
): Message {
  return {
    id: `run-${runId}-assistant`,
    threadId,
    role: 'assistant',
    content: '',
    timestamp,
    isStreaming: true,
    toolProgress,
  };
}

function mergeToolProgress(entries: ToolProgressEvent[], nextEntry: ToolProgressEvent): ToolProgressEvent[] {
  const remaining = entries.filter((entry) => entry.id !== nextEntry.id);
  return [...remaining, nextEntry].sort((left, right) => left.timestamp.localeCompare(right.timestamp));
}

function mergeToolProgressCollections(...collections: ToolProgressEvent[][]): ToolProgressEvent[] {
  return collections
    .flat()
    .reduce<ToolProgressEvent[]>((merged, entry) => mergeToolProgress(merged, entry), []);
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

function mergeHydratedMessages(existing: Message[], incoming: Message[]): Message[] {
  const mergedIncoming = incoming.map((message) => ({ ...message }));
  const incomingIds = new Set(incoming.map((message) => message.id));
  const incomingMatchesAvailable = incoming.map(() => true);
  const existingAssistantToolMessages = existing
    .filter((message) => message.role === 'assistant' && (message.toolProgress?.length ?? 0) > 0)
    .map((message) => ({ ...message, matched: false }));

  for (const incomingMessage of mergedIncoming) {
    if (incomingMessage.role !== 'assistant') {
      continue;
    }

    const exactExisting = existing.find((message) => message.id === incomingMessage.id);
    const exactToolProgress = exactExisting?.toolProgress ?? [];
    if (exactToolProgress.length > 0 || (incomingMessage.toolProgress?.length ?? 0) > 0) {
      incomingMessage.toolProgress = mergeToolProgressCollections(
        exactToolProgress,
        incomingMessage.toolProgress ?? [],
      );
      if (exactExisting) {
        const exactMatch = existingAssistantToolMessages.find((message) => message.id === exactExisting.id);
        if (exactMatch) {
          exactMatch.matched = true;
        }
      }
      continue;
    }

    const heuristicMatch = existingAssistantToolMessages.find((candidate) =>
      !candidate.matched &&
      areTimestampsNear(candidate.timestamp, incomingMessage.timestamp) &&
      (candidate.content === incomingMessage.content ||
        candidate.id.startsWith('run-') ||
        candidate.content.length === 0));
    if (!heuristicMatch) {
      continue;
    }

    heuristicMatch.matched = true;
    incomingMessage.toolProgress = mergeToolProgressCollections(
      heuristicMatch.toolProgress ?? [],
      incomingMessage.toolProgress ?? [],
    );
  }

  const optimisticMessages = existing.filter((message) => {
    if (incomingIds.has(message.id)) {
      return false;
    }

    if (message.id.startsWith('local-user-')) {
      const matchIndex = incoming.findIndex((candidate, index) =>
        incomingMatchesAvailable[index] &&
        candidate.role === 'user' &&
        candidate.content === message.content &&
        areTimestampsNear(candidate.timestamp, message.timestamp));
      if (matchIndex >= 0) {
        incomingMatchesAvailable[matchIndex] = false;
        return false;
      }
    }

    return message.id.startsWith('local-') || message.isStreaming;
  });

  return [...mergedIncoming, ...optimisticMessages]
    .sort((left, right) => left.timestamp.localeCompare(right.timestamp));
}

function areTimestampsNear(left: string, right: string, maxDeltaMs = 2 * 60 * 1000): boolean {
  const leftTime = Date.parse(left);
  const rightTime = Date.parse(right);
  if (Number.isNaN(leftTime) || Number.isNaN(rightTime)) {
    return false;
  }

  return Math.abs(leftTime - rightTime) <= maxDeltaMs;
}

function mergeOlderMessages(existing: Message[], older: Message[]): Message[] {
  const existingIds = new Set(existing.map((message) => message.id));
  return [...older.filter((message) => !existingIds.has(message.id)), ...existing]
    .sort((left, right) => left.timestamp.localeCompare(right.timestamp));
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

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  if (typeof error === 'string' && error.trim()) {
    return error;
  }

  if (error && typeof error === 'object') {
    const candidate = Reflect.get(error, 'message');
    if (typeof candidate === 'string' && candidate.trim()) {
      return candidate;
    }
  }

  return fallback;
}

function pickDefinedSettings(partial: Partial<SettingsState>): Partial<SettingsState> {
  return Object.fromEntries(
    Object.entries(partial).filter(([, value]) => value !== undefined),
  ) as Partial<SettingsState>;
}

function capturePassiveSettingsSnapshotVersion(): number {
  return settingsMutationVersion;
}

function canApplyPassiveSettingsSnapshot(version: number): boolean {
  return settingsMutationsInFlight === 0 && version === settingsMutationVersion;
}

function beginSettingsMutation() {
  settingsMutationsInFlight += 1;
  settingsMutationVersion += 1;
}

function endSettingsMutation() {
  settingsMutationsInFlight = Math.max(0, settingsMutationsInFlight - 1);
  settingsMutationVersion += 1;
}
