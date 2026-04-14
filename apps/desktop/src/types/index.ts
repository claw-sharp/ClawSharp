export type ThreadStatus = 'idle' | 'running' | 'waiting_approval' | 'completed' | 'failed' | 'scheduled';
export type ThreadTarget = 'local' | 'worktree' | 'cloud';
export type MessageRole = 'user' | 'assistant' | 'system' | 'tool';
export type FileChangeStatus = 'A' | 'M' | 'D';
export type InboxItemType = 'review' | 'automation' | 'error' | 'info';
export type AutomationCadence = 'daily' | 'weekly' | 'hourly' | 'on_push' | 'manual';
export type PluginOptionType = 'string' | 'number' | 'boolean' | 'directory' | 'file';

export interface Project {
  id: string;
  name: string;
  path: string;
  branch?: string | null;
  activeThreadCount: number;
  lastUpdated: string;
  description?: string;
}

export interface Thread {
  id: string;
  projectId: string;
  title: string;
  summary: string;
  status: ThreadStatus;
  changedFilesCount: number;
  target: ThreadTarget;
  lastUpdated: string;
  provider: string;
  model: string;
  pinned: boolean;
}

export interface ToolProgressEvent {
  id: string;
  type: 'reading' | 'planning' | 'editing' | 'reviewing' | 'finalizing' | 'searching' | 'testing' | 'running' | 'waiting' | 'tool';
  toolName?: string;
  label: string;
  input?: string;
  detail?: string;
  timestamp: string;
  completed: boolean;
  status?: 'running' | 'completed' | 'failed';
}

export interface Message {
  id: string;
  threadId: string;
  role: MessageRole;
  content: string;
  timestamp: string;
  toolProgress?: ToolProgressEvent[];
  isStreaming?: boolean;
}

export interface ThreadHistoryState {
  hasMoreMessages: boolean;
  nextBeforeMessageId: string | null;
  isLoadingOlder: boolean;
}

export interface ChangedFile {
  path: string;
  status: FileChangeStatus;
  additions: number;
  deletions: number;
  threadId: string;
}

export interface DiffHunk {
  header: string;
  lines: DiffLine[];
}

export interface DiffLine {
  type: 'add' | 'del' | 'context';
  content: string;
  oldLineNumber?: number;
  newLineNumber?: number;
}

export interface DiffChunk {
  filePath: string;
  hunks: DiffHunk[];
}

export interface ReviewComment {
  id: string;
  filePath: string;
  line: number;
  content: string;
  author: string;
  timestamp: string;
}

export interface LogEntry {
  id: string;
  threadId: string;
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  stage: string;
  message: string;
}

export interface DiagnosticsRecord {
  sessionId: string;
  runId: string;
  configPath: string;
  provider: string;
  model: string;
  baseUrl: string;
  transport: string;
  environment: string;
  warnings: string[];
  errors: string[];
  uptime: string;
  memoryUsage: string;
  threadId: string;
  debugLogPath: string;
  telemetryEventsPath: string;
  metricsPath: string;
  crashPath: string;
  tracePath: string;
  startupProfilePath: string;
}

export interface PluginValidationIssue {
  path: string;
  message: string;
  isWarning: boolean;
}

export interface PluginOption {
  key: string;
  type: PluginOptionType;
  title: string;
  description: string;
  required: boolean;
  multiple: boolean;
  sensitive: boolean;
  hasValue: boolean;
  value?: unknown;
  defaultValue?: unknown;
  min?: number | null;
  max?: number | null;
}

export interface PluginMcpServer {
  name: string;
  type: string;
  endpoint?: string | null;
}

export interface Plugin {
  pluginId: string;
  name: string;
  description?: string | null;
  version?: string | null;
  enabled: boolean;
  authenticated?: boolean;
  isBundled: boolean;
  installPath: string;
  scope: string;
  installedAt?: string | null;
  lastUpdated?: string | null;
  gitCommitSha?: string | null;
  commands: string[];
  agents: string[];
  skills: string[];
  outputStyles: string[];
  hookFiles: string[];
  hookEvents: string[];
  validationIssues: PluginValidationIssue[];
  options: PluginOption[];
  mcpServers: PluginMcpServer[];
}

export interface Skill {
  name: string;
  source: string;
  filePath: string;
  baseDirectory: string;
}

export interface ProviderOption {
  id: string;
  displayName: string;
  defaultModel: string;
  models: string[];
  baseUrl: string;
  requiresApiKey: boolean;
  description: string;
}

export interface ProviderCredentialState {
  hasApiKey: boolean;
  hasAuthToken: boolean;
  accountId?: string | null;
  source: 'none' | 'saved' | 'external';
  hasExternalCredential: boolean;
  externalCredentialPath?: string | null;
}

export interface Automation {
  id: string;
  projectId: string;
  title: string;
  cadence: AutomationCadence;
  lastRun: string;
  status: 'active' | 'paused' | 'error';
  resultSummary: string;
  linkedThreadId?: string;
}

export interface InboxItem {
  id: string;
  type: InboxItemType;
  title: string;
  summary: string;
  timestamp: string;
  read: boolean;
  projectId: string;
  threadId?: string;
  automationId?: string;
  approvalId?: string;
}

export interface SettingsState {
  theme: 'dark' | 'light' | 'system';
  density: 'compact' | 'comfortable' | 'spacious';
  defaultProvider: string;
  defaultModel: string;
  fallbackModel?: string | null;
  permissionMode?: string;
  providerBaseUrl?: string;
  providerTransport?: string;
  configPath?: string;
  settingsIssues: string[];
  providerValidationWarnings: string[];
  providerValidationErrors: string[];
  availableProviders: ProviderOption[];
  providerCredentials: ProviderCredentialState;
  hasAnyConfiguredProviderCredential: boolean;
  showDiagnostics: boolean;
  streamingSpeed: 'slow' | 'normal' | 'fast';
  compactMode: boolean;
  reducedMotion: boolean;
  notifications: boolean;
  editorPath: string;
}

export interface NavigationLoadingState {
  requestId: string;
  kind: 'project' | 'thread';
  title: string;
  description: string;
}

export interface UIState {
  leftSidebarCollapsed: boolean;
  rightPanelCollapsed: boolean;
  rightPanelTab: 'files' | 'diff';
  bottomDrawerTab: 'logs' | 'terminal' | 'diagnostics';
  bottomDrawerOpen: boolean;
  settingsOpen: boolean;
  commandPaletteOpen: boolean;
  selectedChangedFile: string | null;
  selectedInboxItem: string | null;
  activeView: 'threads' | 'inbox' | 'automations' | 'plugins' | 'settings';
  navigationLoading: NavigationLoadingState | null;
}

export interface RunState {
  activeRunId: string | null;
  activeThreadId: string | null;
  isRunning: boolean;
  isStreaming: boolean;
  pendingApproval: boolean;
  progressLabel: string;
  changedFilesCount: number;
  toolProgress: ToolProgressEvent[];
  errorMessage?: string | null;
}

export interface ConnectionState {
  isConnected: boolean;
  isBootstrapping: boolean;
  lastEventAt: string | null;
  errorMessage: string | null;
  statusLabel: string;
}
