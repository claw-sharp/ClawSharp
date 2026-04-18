import type {
  MobileApproval,
  MobileChangedFile,
  MobileDiff,
  MobileProject,
  MobileRunEvent,
  MobileThread,
  MobileThreadDetail,
} from './types';

export class MobileApiClient {
  constructor(private readonly baseUrl: string, private readonly token?: string | null) {}

  async listProjects(): Promise<MobileProject[]> {
    return await this.get('/v1/projects');
  }

  async listThreads(projectId: string): Promise<MobileThread[]> {
    return await this.get(`/v1/projects/${encodeURIComponent(projectId)}/threads`);
  }

  async getThread(projectId: string, threadId: string): Promise<MobileThreadDetail> {
    return await this.get(`/v1/projects/${encodeURIComponent(projectId)}/threads/${encodeURIComponent(threadId)}`);
  }

  async createThread(projectId: string, title?: string): Promise<MobileThreadDetail> {
    return await this.post('/v1/threads', { projectId, title: title ?? null });
  }

  async listApprovals(threadId?: string): Promise<MobileApproval[]> {
    const query = threadId ? `?threadId=${encodeURIComponent(threadId)}` : '';
    return await this.get(`/v1/approvals${query}`);
  }

  async resolveApproval(approvalId: string, decision: 'Approved' | 'AlwaysAllow' | 'Rejected'): Promise<MobileApproval> {
    return await this.post('/v1/approvals/resolve', { approvalId, decision });
  }

  async listChangedFiles(projectId: string, threadId?: string): Promise<MobileChangedFile[]> {
    const query = threadId ? `?threadId=${encodeURIComponent(threadId)}` : '';
    return await this.get(`/v1/projects/${encodeURIComponent(projectId)}/changed-files${query}`);
  }

  async getDiff(projectId: string, filePath: string, threadId?: string): Promise<MobileDiff> {
    const query = new URLSearchParams({ filePath });
    if (threadId) {
      query.set('threadId', threadId);
    }

    return await this.get(`/v1/projects/${encodeURIComponent(projectId)}/diff?${query.toString()}`);
  }

  async startRun(projectId: string, threadId: string, prompt: string): Promise<{ runId: string }> {
    return await this.post('/v1/runs/start', { projectId, threadId, prompt });
  }

  streamThread(threadId: string, onEvent: (event: MobileRunEvent) => void): () => void {
    const source = new EventSource(`${this.baseUrl.replace(/\/$/, '')}/v1/threads/${encodeURIComponent(threadId)}/events`);
    source.addEventListener('run', (event) => {
      const messageEvent = event as MessageEvent<string>;
      onEvent(JSON.parse(messageEvent.data) as MobileRunEvent);
    });

    return () => source.close();
  }

  private async get<T>(path: string): Promise<T> {
    const response = await fetch(`${this.baseUrl.replace(/\/$/, '')}${path}`, {
      headers: this.buildHeaders(),
    });
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }
    return await response.json() as T;
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const response = await fetch(`${this.baseUrl.replace(/\/$/, '')}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.buildHeaders(),
      },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }
    return await response.json() as T;
  }

  private buildHeaders(): Record<string, string> {
    if (!this.token) {
      return {};
    }

    return {
      Authorization: `Bearer ${this.token}`,
    };
  }
}
