import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { RemoteApiClient } from '@/lib/remoteApiClient';

class MockEventSource {
  public static instances: MockEventSource[] = [];
  public listeners = new Map<string, Array<(event: MessageEvent<string>) => void>>();
  public onerror: (() => void) | null = null;

  constructor(public readonly url: string) {
    MockEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close() {}

  emit(type: string, data: unknown) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data: JSON.stringify(data) } as MessageEvent<string>);
    }
  }
}

describe('RemoteApiClient', () => {
  beforeEach(() => {
    MockEventSource.instances = [];
    vi.stubGlobal('EventSource', MockEventSource);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('maps recent projects from the remote API', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true,
      json: async () => ([
        {
          id: 'project-demo',
          name: 'ClawSharp',
          path: '/workspace/ClawSharp',
          gitBranch: 'mobile-app',
          threadCount: 2,
          lastUpdatedAt: '2026-04-18T10:00:00Z',
        },
      ]),
    })));

    const client = new RemoteApiClient('http://127.0.0.1:5055');
    const response = await client.listRecentProjects();

    expect(response.projects).toEqual([
      {
        id: 'project-demo',
        name: 'ClawSharp',
        path: '/workspace/ClawSharp',
        gitBranch: 'mobile-app',
        lastOpenedAt: '2026-04-18T10:00:00Z',
        lastUpdatedAt: '2026-04-18T10:00:00Z',
        threadCount: 2,
      },
    ]);
  });

  it('forwards streamed remote run events into desktop event envelopes', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.endsWith('/v1/runs/start')) {
        return {
          ok: true,
          json: async () => ({
            runId: 'run-demo',
            threadId: 'thread-demo',
            acceptedAt: '2026-04-18T10:00:00Z',
          }),
        };
      }

      return {
        ok: true,
        json: async () => ({
          service: 'ClawSharp.Api',
          status: 'ok',
          supportedClientModes: ['desktop-remote', 'mobile-remote'],
        }),
      };
    }));

    const client = new RemoteApiClient('http://127.0.0.1:5055');
    const onEvent = vi.fn();
    const onState = vi.fn();

    await client.subscribe(onEvent, onState);
    await client.startRun('project-demo', 'thread-demo', 'Test remote run');

    expect(MockEventSource.instances).toHaveLength(1);

    MockEventSource.instances[0].emit('run', {
      runId: 'run-demo',
      threadId: 'thread-demo',
      kind: 'TextDelta',
      timestamp: '2026-04-18T10:00:01Z',
      textDelta: 'Hello remote ',
    });

    expect(onEvent).toHaveBeenCalledWith({
      event: 'RunTextDelta',
      timestamp: '2026-04-18T10:00:01Z',
      payload: {
        runId: 'run-demo',
        threadId: 'thread-demo',
        delta: 'Hello remote ',
        timestamp: '2026-04-18T10:00:01Z',
      },
    });
  });
});
