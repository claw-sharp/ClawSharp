export type ThreadStatus = 'idle' | 'running' | 'waiting_approval' | 'completed' | 'failed' | 'scheduled';
export type ThreadTarget = 'local' | 'worktree' | 'cloud';
export type MessageRole = 'user' | 'assistant' | 'system' | 'tool';
export type FileChangeStatus = 'A' | 'M' | 'D';
export type InboxItemType = 'review' | 'automation' | 'error' | 'info';
export type AutomationCadence = 'daily' | 'weekly' | 'hourly' | 'on_push' | 'manual';

export interface Project {
  id: string;
  name: string;
  path: string;
  branch: string;
  activeThreadCount: number;
  lastUpdated: string;
  description: string;
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
  type: 'reading' | 'planning' | 'editing' | 'reviewing' | 'finalizing' | 'searching' | 'testing';
  label: string;
  detail?: string;
  timestamp: string;
  completed: boolean;
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
  environment: string;
  warnings: string[];
  errors: string[];
  uptime: string;
  memoryUsage: string;
  threadId: string;
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
}

export interface SettingsState {
  theme: 'dark' | 'light' | 'system';
  density: 'compact' | 'comfortable' | 'spacious';
  defaultProvider: string;
  defaultModel: string;
  showDiagnostics: boolean;
  streamingSpeed: 'slow' | 'normal' | 'fast';
  compactMode: boolean;
  reducedMotion: boolean;
  notifications: boolean;
  editorPath: string;
}

export interface UIState {
  leftSidebarCollapsed: boolean;
  rightPanelTab: 'files' | 'diff';
  bottomDrawerTab: 'logs' | 'terminal' | 'diagnostics';
  bottomDrawerOpen: boolean;
  settingsOpen: boolean;
  commandPaletteOpen: boolean;
  selectedChangedFile: string | null;
  selectedInboxItem: string | null;
  activeView: 'threads' | 'inbox' | 'automations' | 'settings';
}

export interface RunState {
  activeRunId: string | null;
  isRunning: boolean;
  isStreaming: boolean;
  pendingApproval: boolean;
  progressLabel: string;
  changedFilesCount: number;
}
