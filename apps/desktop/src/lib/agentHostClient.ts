import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import { BrowserAgentHostClient } from '@/lib/browserAgentHostClient';
import {
  subscribeAgentHostEvents,
  subscribeAgentHostState,
} from '@/lib/eventStream';
import type {
  AgentHostCommandMap,
  AgentHostEventEnvelope,
  AgentHostStateEvent,
  CancelRunResponse,
  GetDiffResponse,
  GetSettingsResponse,
  ListChangedFilesResponse,
  ListDiagnosticsResponse,
  ListPendingApprovalsResponse,
  ListProvidersResponse,
  OpenExternalEditorRequest,
  OpenExternalEditorResponse,
  CreateThreadResponse,
  GetThreadResponse,
  HealthResponse,
  ListRecentProjectsResponse,
  ListThreadsResponse,
  OpenProjectResponse,
  RenameThreadResponse,
  ResolveApprovalResponse,
  StartRunResponse,
  UpdateSettingsRequest,
  UpdateSettingsResponse,
  ValidateProviderConfigResponse,
} from '@/lib/protocol';

function isTauriRuntime(): boolean {
  if (typeof window === 'undefined') {
    return false;
  }

  return '__TAURI_INTERNALS__' in window;
}

class AgentHostClient {
  async connect(): Promise<HealthResponse> {
    return await this.request('health', {});
  }

  async openProject(projectPath: string): Promise<OpenProjectResponse> {
    return await this.request('openProject', { projectPath });
  }

  async listRecentProjects(): Promise<ListRecentProjectsResponse> {
    return await this.request('listRecentProjects', {});
  }

  async listThreads(projectId: string): Promise<ListThreadsResponse> {
    return await this.request('listThreads', { projectId });
  }

  async createThread(projectId: string, title?: string | null): Promise<CreateThreadResponse> {
    return await this.request('createThread', { projectId, title: title ?? null });
  }

  async getThread(projectId: string, threadId: string): Promise<GetThreadResponse> {
    return await this.request('getThread', { projectId, threadId });
  }

  async renameThread(projectId: string, threadId: string, title: string): Promise<RenameThreadResponse> {
    return await this.request('renameThread', { projectId, threadId, title });
  }

  async archiveThread(projectId: string, threadId: string) {
    return await this.request('archiveThread', { projectId, threadId });
  }

  async startRun(projectId: string, threadId: string, prompt: string): Promise<StartRunResponse> {
    return await this.request('startRun', { projectId, threadId, prompt });
  }

  async cancelRun(runId: string): Promise<CancelRunResponse> {
    return await this.request('cancelRun', { runId });
  }

  async retryRun(threadId: string, projectId?: string | null, fromMessageId?: string | null): Promise<StartRunResponse> {
    return await this.request('retryRun', { threadId, projectId: projectId ?? null, fromMessageId: fromMessageId ?? null });
  }

  async listChangedFiles(projectId: string, threadId?: string | null): Promise<ListChangedFilesResponse> {
    return await this.request('listChangedFiles', { projectId, threadId: threadId ?? null });
  }

  async getDiff(projectId: string, filePath: string, threadId?: string | null): Promise<GetDiffResponse> {
    return await this.request('getDiff', { projectId, filePath, threadId: threadId ?? null });
  }

  async openExternalEditor(request: OpenExternalEditorRequest): Promise<OpenExternalEditorResponse> {
    return await this.request('openExternalEditor', request);
  }

  async listDiagnostics(projectId?: string | null, threadId?: string | null): Promise<ListDiagnosticsResponse> {
    return await this.request('listDiagnostics', { projectId: projectId ?? null, threadId: threadId ?? null });
  }

  async getSettings(projectId?: string | null): Promise<GetSettingsResponse> {
    return await this.request('getSettings', { projectId: projectId ?? null });
  }

  async updateSettings(request: UpdateSettingsRequest): Promise<UpdateSettingsResponse> {
    return await this.request('updateSettings', request);
  }

  async listProviders(): Promise<ListProvidersResponse> {
    return await this.request('listProviders', {});
  }

  async validateProviderConfig(
    request: {
      projectId?: string | null;
      provider: string;
      model?: string | null;
      liveCheck?: boolean | null;
      apiKey?: string | null;
      authToken?: string | null;
      accountId?: string | null;
      useExternalCredential?: boolean | null;
    },
  ): Promise<ValidateProviderConfigResponse> {
    return await this.request('validateProviderConfig', request);
  }

  async listPendingApprovals(threadId?: string | null): Promise<ListPendingApprovalsResponse> {
    return await this.request('listPendingApprovals', { threadId: threadId ?? null });
  }

  async resolveApproval(approvalId: string, decision: 'approved' | 'rejected'): Promise<ResolveApprovalResponse> {
    return await this.request('resolveApproval', { approvalId, decision });
  }

  async pickProjectDirectory(): Promise<string | null> {
    const selection = await open({
      directory: true,
      multiple: false,
      title: 'Open Project',
    });

    return typeof selection === 'string' ? selection : null;
  }

  async request<TCommand extends keyof AgentHostCommandMap>(
    command: TCommand,
    payload: AgentHostCommandMap[TCommand]['request'],
  ): Promise<AgentHostCommandMap[TCommand]['response']> {
    return await invoke<AgentHostCommandMap[TCommand]['response']>('agent_host_request', { command, payload });
  }

  async subscribe(
    onEvent: (event: AgentHostEventEnvelope) => void,
    onState: (event: AgentHostStateEvent) => void,
  ): Promise<() => void> {
    const [unlistenEvents, unlistenState] = await Promise.all([
      subscribeAgentHostEvents(onEvent),
      subscribeAgentHostState(onState),
    ]);

    return () => {
      unlistenEvents();
      unlistenState();
    };
  }
}

export const agentHostClient = isTauriRuntime()
  ? new AgentHostClient()
  : new BrowserAgentHostClient();
