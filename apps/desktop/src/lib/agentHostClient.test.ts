import { beforeEach, describe, expect, it, vi } from 'vitest';

const invoke = vi.fn();
const listen = vi.fn();

vi.mock('@tauri-apps/api/core', () => ({
  invoke,
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen,
}));

describe('agentHostClient', () => {
  beforeEach(() => {
    invoke.mockReset();
    listen.mockReset();
  });

  it('sends typed requests through the tauri bridge', async () => {
    invoke.mockResolvedValue({ hostName: 'ClawSharp.AgentHost' });

    const { agentHostClient } = await import('@/lib/agentHostClient');
    const response = await agentHostClient.connect();

    expect(invoke).toHaveBeenCalledWith('agent_host_request', {
      command: 'health',
      payload: {},
    });
    expect(response).toEqual({ hostName: 'ClawSharp.AgentHost' });
  });

  it('subscribes to host and state event streams', async () => {
    const unlisten = vi.fn();
    listen.mockResolvedValue(unlisten);

    const { agentHostClient } = await import('@/lib/agentHostClient');
    const dispose = await agentHostClient.subscribe(() => undefined, () => undefined);

    expect(listen).toHaveBeenCalledTimes(2);
    dispose();
    expect(unlisten).toHaveBeenCalledTimes(2);
  });
});
