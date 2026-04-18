import type {
  AgentHostCommandMap,
  AgentHostEventEnvelope,
  AgentHostStateEvent,
} from '@/lib/protocol';

export interface RuntimeClient {
  connect(): Promise<AgentHostCommandMap['health']['response']>;
  openProject(projectPath: string): Promise<AgentHostCommandMap['openProject']['response']>;
  listRecentProjects(): Promise<AgentHostCommandMap['listRecentProjects']['response']>;
  listThreads(projectId: string): Promise<AgentHostCommandMap['listThreads']['response']>;
  createThread(projectId: string, title?: string | null): Promise<AgentHostCommandMap['createThread']['response']>;
  getThread(
    projectId: string,
    threadId: string,
    options?: { beforeMessageId?: string | null; pageSize?: number | null },
  ): Promise<AgentHostCommandMap['getThread']['response']>;
  renameThread(projectId: string, threadId: string, title: string): Promise<AgentHostCommandMap['renameThread']['response']>;
  archiveThread(projectId: string, threadId: string): Promise<AgentHostCommandMap['archiveThread']['response']>;
  startRun(projectId: string, threadId: string, prompt: string): Promise<AgentHostCommandMap['startRun']['response']>;
  cancelRun(runId: string): Promise<AgentHostCommandMap['cancelRun']['response']>;
  retryRun(
    threadId: string,
    projectId?: string | null,
    fromMessageId?: string | null,
  ): Promise<AgentHostCommandMap['retryRun']['response']>;
  listChangedFiles(projectId: string, threadId?: string | null): Promise<AgentHostCommandMap['listChangedFiles']['response']>;
  getDiff(projectId: string, filePath: string, threadId?: string | null): Promise<AgentHostCommandMap['getDiff']['response']>;
  openExternalEditor(
    request: AgentHostCommandMap['openExternalEditor']['request'],
  ): Promise<AgentHostCommandMap['openExternalEditor']['response']>;
  listDiagnostics(
    projectId?: string | null,
    threadId?: string | null,
  ): Promise<AgentHostCommandMap['listDiagnostics']['response']>;
  listPlugins(projectId?: string | null): Promise<AgentHostCommandMap['listPlugins']['response']>;
  listAgents(projectId?: string | null): Promise<AgentHostCommandMap['listAgents']['response']>;
  listSkills(projectId?: string | null): Promise<AgentHostCommandMap['listSkills']['response']>;
  createAgent(
    request: AgentHostCommandMap['createAgent']['request'],
  ): Promise<AgentHostCommandMap['createAgent']['response']>;
  createSkill(
    request: AgentHostCommandMap['createSkill']['request'],
  ): Promise<AgentHostCommandMap['createSkill']['response']>;
  proposeAgent(
    request: AgentHostCommandMap['proposeAgent']['request'],
  ): Promise<AgentHostCommandMap['proposeAgent']['response']>;
  listWorkspaceFiles(projectId?: string | null): Promise<AgentHostCommandMap['listWorkspaceFiles']['response']>;
  getSettings(projectId?: string | null): Promise<AgentHostCommandMap['getSettings']['response']>;
  updateSettings(
    request: AgentHostCommandMap['updateSettings']['request'],
  ): Promise<AgentHostCommandMap['updateSettings']['response']>;
  installPlugin(
    request: AgentHostCommandMap['installPlugin']['request'],
  ): Promise<AgentHostCommandMap['installPlugin']['response']>;
  setPluginEnabled(
    request: AgentHostCommandMap['setPluginEnabled']['request'],
  ): Promise<AgentHostCommandMap['setPluginEnabled']['response']>;
  savePluginOptions(
    request: AgentHostCommandMap['savePluginOptions']['request'],
  ): Promise<AgentHostCommandMap['savePluginOptions']['response']>;
  deletePluginOptions(
    request: AgentHostCommandMap['deletePluginOptions']['request'],
  ): Promise<AgentHostCommandMap['deletePluginOptions']['response']>;
  refreshPlugins(projectId?: string | null): Promise<AgentHostCommandMap['refreshPlugins']['response']>;
  listProviders(): Promise<AgentHostCommandMap['listProviders']['response']>;
  validateProviderConfig(
    request: AgentHostCommandMap['validateProviderConfig']['request'],
  ): Promise<AgentHostCommandMap['validateProviderConfig']['response']>;
  listPendingApprovals(threadId?: string | null): Promise<AgentHostCommandMap['listPendingApprovals']['response']>;
  resolveApproval(
    approvalId: string,
    decision: 'approved' | 'always_allow' | 'rejected',
  ): Promise<AgentHostCommandMap['resolveApproval']['response']>;
  pickProjectDirectory(): Promise<string | null>;
  pickPromptFiles(): Promise<string[]>;
  pickPromptImages(): Promise<string[]>;
  subscribe(
    onEvent: (event: AgentHostEventEnvelope) => void,
    onState: (event: AgentHostStateEvent) => void,
  ): Promise<() => void>;
}
