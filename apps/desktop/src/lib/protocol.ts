export interface AgentHostCommandMap {
  health: { request: Record<string, never>; response: HealthResponse };
  openProject: { request: OpenProjectRequest; response: OpenProjectResponse };
  listRecentProjects: { request: Record<string, never>; response: ListRecentProjectsResponse };
  listThreads: { request: ListThreadsRequest; response: ListThreadsResponse };
  createThread: { request: CreateThreadRequest; response: CreateThreadResponse };
  getThread: { request: GetThreadRequest; response: GetThreadResponse };
  renameThread: { request: RenameThreadRequest; response: RenameThreadResponse };
  archiveThread: { request: ArchiveThreadRequest; response: ArchiveThreadResponse };
  startRun: { request: StartRunRequest; response: StartRunResponse };
  cancelRun: { request: CancelRunRequest; response: CancelRunResponse };
  retryRun: { request: RetryRunRequest; response: StartRunResponse };
  listChangedFiles: { request: ListChangedFilesRequest; response: ListChangedFilesResponse };
  getDiff: { request: GetDiffRequest; response: GetDiffResponse };
  openExternalEditor: { request: OpenExternalEditorRequest; response: OpenExternalEditorResponse };
  listDiagnostics: { request: ListDiagnosticsRequest; response: ListDiagnosticsResponse };
  getSettings: { request: GetSettingsRequest; response: GetSettingsResponse };
  updateSettings: { request: UpdateSettingsRequest; response: UpdateSettingsResponse };
  listProviders: { request: Record<string, never>; response: ListProvidersResponse };
  validateProviderConfig: { request: ValidateProviderConfigRequest; response: ValidateProviderConfigResponse };
  listPendingApprovals: { request: ListPendingApprovalsRequest; response: ListPendingApprovalsResponse };
  resolveApproval: { request: ResolveApprovalRequest; response: ResolveApprovalResponse };
}

export interface HealthResponse {
  hostName: string;
  hostVersion: string;
  protocolVersion: string;
  currentWorkingDirectory: string;
  supportedCommands: string[];
}

export interface OpenProjectRequest {
  projectPath: string;
}

export interface OpenProjectResponse {
  project: AgentHostProject;
  threads: AgentHostThreadSummary[];
}

export interface ListRecentProjectsResponse {
  projects: AgentHostProject[];
}

export interface ListThreadsRequest {
  projectId: string;
}

export interface ListThreadsResponse {
  project: AgentHostProject;
  threads: AgentHostThreadSummary[];
}

export interface CreateThreadRequest {
  projectId: string;
  title?: string | null;
}

export interface CreateThreadResponse {
  project: AgentHostProject;
  thread: AgentHostThreadDetail;
}

export interface GetThreadRequest {
  threadId: string;
  projectId?: string | null;
}

export interface GetThreadResponse {
  project: AgentHostProject;
  thread: AgentHostThreadDetail;
}

export interface RenameThreadRequest {
  projectId: string;
  threadId: string;
  title: string;
}

export interface RenameThreadResponse {
  project: AgentHostProject;
  thread: AgentHostThreadDetail;
}

export interface ArchiveThreadRequest {
  projectId: string;
  threadId: string;
}

export interface ArchiveThreadResponse {
  projectId: string;
  threadId: string;
  archived: boolean;
  timestamp: string;
}

export interface StartRunRequest {
  threadId: string;
  projectId: string;
  prompt: string;
}

export interface StartRunResponse {
  runId: string;
  threadId: string;
  acceptedAt: string;
}

export interface CancelRunRequest {
  runId: string;
}

export interface CancelRunResponse {
  runId: string;
  cancelled: boolean;
  timestamp: string;
}

export interface RetryRunRequest {
  threadId: string;
  projectId?: string | null;
  fromMessageId?: string | null;
}

export interface ListChangedFilesRequest {
  projectId: string;
  threadId?: string | null;
}

export interface GetDiffRequest {
  projectId: string;
  filePath: string;
  threadId?: string | null;
}

export interface OpenExternalEditorRequest {
  kind: 'project' | 'file' | 'position' | 'diff';
  projectId?: string | null;
  path?: string | null;
  line?: number | null;
  column?: number | null;
  leftPath?: string | null;
  rightPath?: string | null;
  editorCommand?: string | null;
}

export interface ListDiagnosticsRequest {
  projectId?: string | null;
  threadId?: string | null;
}

export interface GetSettingsRequest {
  projectId?: string | null;
}

export interface UpdateSettingsRequest {
  projectId?: string | null;
  provider?: string | null;
  model?: string | null;
  fallbackModel?: string | null;
  enableTelemetry?: boolean | null;
  fileCheckpointingEnabled?: boolean | null;
  apiKey?: string | null;
  authToken?: string | null;
  accountId?: string | null;
  clearApiKey?: boolean | null;
  clearAuthToken?: boolean | null;
  clearAccountId?: boolean | null;
  useExternalCredential?: boolean | null;
}

export interface ValidateProviderConfigRequest {
  projectId?: string | null;
  provider: string;
  model?: string | null;
  liveCheck?: boolean | null;
  apiKey?: string | null;
  authToken?: string | null;
  accountId?: string | null;
  useExternalCredential?: boolean | null;
}

export interface ListPendingApprovalsRequest {
  threadId?: string | null;
}

export interface ResolveApprovalRequest {
  approvalId: string;
  decision: 'approved' | 'rejected';
}

export interface AgentHostProject {
  id: string;
  name: string;
  path: string;
  lastOpenedAt: string;
  lastUpdatedAt?: string | null;
  threadCount: number;
  gitBranch?: string | null;
}

export interface AgentHostThreadWorktree {
  repoRoot: string;
  worktreePath?: string | null;
  worktreeStatus?: string | null;
  baseBranch?: string | null;
  baseCommit?: string | null;
}

export interface AgentHostThreadSummary {
  id: string;
  projectId: string;
  title: string;
  summary: string;
  lastUpdatedAt: string;
  messageCount: number;
  transcriptPath: string;
  worktree: AgentHostThreadWorktree;
}

export interface AgentHostThreadMessage {
  id: string;
  threadId: string;
  role: string;
  content: string;
  timestamp: string;
}

export interface AgentHostThreadDetail {
  thread: AgentHostThreadSummary;
  messages: AgentHostThreadMessage[];
}

export interface AgentHostChangedFile {
  path: string;
  status: 'A' | 'M' | 'D';
  additions: number;
  deletions: number;
  threadId: string;
}

export interface AgentHostDiffLine {
  type: 'add' | 'del' | 'context';
  content: string;
  oldLineNumber?: number | null;
  newLineNumber?: number | null;
}

export interface AgentHostDiffHunk {
  header: string;
  lines: AgentHostDiffLine[];
}

export interface AgentHostDiff {
  filePath: string;
  hunks: AgentHostDiffHunk[];
}

export interface ListChangedFilesResponse {
  projectId: string;
  threadId?: string | null;
  files: AgentHostChangedFile[];
}

export interface GetDiffResponse {
  projectId: string;
  threadId?: string | null;
  diff: AgentHostDiff;
}

export interface AgentHostExternalEditorLaunch {
  launched: boolean;
  command: string;
  arguments: string[];
  message: string;
}

export interface OpenExternalEditorResponse {
  launch: AgentHostExternalEditorLaunch;
}

export interface AgentHostDiagnosticsLogPaths {
  debugLogPath: string;
  telemetryEventsPath: string;
  metricsPath: string;
  crashPath: string;
  tracePath: string;
  startupProfilePath: string;
}

export interface AgentHostDiagnosticsEvent {
  id: string;
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  stage: string;
  message: string;
}

export interface AgentHostDiagnostics {
  threadId?: string | null;
  sessionId?: string | null;
  provider: string;
  model: string;
  baseUrl: string;
  transport: string;
  environment: string;
  configPath: string;
  uptime: string;
  memoryUsage: string;
  logPaths: AgentHostDiagnosticsLogPaths;
  warnings: string[];
  errors: string[];
  recentEvents: AgentHostDiagnosticsEvent[];
}

export interface ListDiagnosticsResponse {
  diagnostics: AgentHostDiagnostics;
}

export interface AgentHostRuntimeSettings {
  provider: string;
  model: string;
  fallbackModel?: string | null;
  permissionMode: string;
  enableTelemetry: boolean;
  fileCheckpointingEnabled: boolean;
  baseUrl: string;
  transport: string;
  configPath: string;
  settingsIssues: string[];
  credentials: AgentHostProviderCredentials;
  hasAnyConfiguredProviderCredential?: boolean;
}

export interface GetSettingsResponse {
  settings: AgentHostRuntimeSettings;
}

export interface UpdateSettingsResponse {
  settings: AgentHostRuntimeSettings;
}

export interface AgentHostProviderOption {
  id: string;
  displayName: string;
  defaultModel: string;
  models: string[];
  baseUrl: string;
  requiresApiKey: boolean;
  description: string;
}

export interface ListProvidersResponse {
  providers: AgentHostProviderOption[];
}

export interface AgentHostProviderValidation {
  provider: string;
  isValid: boolean;
  errors: string[];
  warnings: string[];
}

export interface AgentHostProviderCredentials {
  hasApiKey: boolean;
  hasAuthToken: boolean;
  accountId?: string | null;
  source: 'none' | 'saved' | 'external';
  hasExternalCredential: boolean;
  externalCredentialPath?: string | null;
}

export interface ValidateProviderConfigResponse {
  validation: AgentHostProviderValidation;
}

export interface AgentHostApprovalRequest {
  id: string;
  action: string;
  decision: string;
  createdAt: string;
}

export interface ListPendingApprovalsResponse {
  approvals: AgentHostApprovalRequest[];
}

export interface ResolveApprovalResponse {
  approval: AgentHostApprovalRequest;
}

export interface AgentHostEventEnvelope {
  event: string;
  timestamp: string;
  payload: unknown;
}

export interface HostReadyEvent {
  hostName: string;
  hostVersion: string;
  protocolVersion: string;
}

export interface AgentHostStateEvent {
  status: string;
  detail?: string | null;
}

export interface RunStartedEvent {
  runId: string;
  threadId: string;
  projectId: string;
  prompt: string;
  timestamp: string;
}

export interface RunTextDeltaEvent {
  runId: string;
  threadId: string;
  delta: string;
  timestamp: string;
}

export interface RunMessageCompletedEvent {
  runId: string;
  threadId: string;
  message: AgentHostThreadMessage;
  timestamp: string;
}

export interface RunToolProgressEvent {
  runId: string;
  threadId: string;
  toolUseId: string;
  parentToolUseId?: string | null;
  toolName: string;
  label: string;
  detail?: string | null;
  stage: string;
  timestamp: string;
}

export interface RunToolResultEvent {
  runId: string;
  threadId: string;
  toolUseId: string;
  toolName: string;
  success: boolean;
  content: string;
  timestamp: string;
}

export interface RunCompletedEvent {
  runId: string;
  threadId: string;
  reason: string;
  errorMessage?: string | null;
  timestamp: string;
}

export interface RunFailedEvent {
  runId: string;
  threadId: string;
  errorMessage: string;
  timestamp: string;
}
