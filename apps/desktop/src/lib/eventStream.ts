import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import type { AgentHostEventEnvelope, AgentHostStateEvent } from '@/lib/protocol';

const AGENT_HOST_EVENT = 'agenthost://event';
const AGENT_HOST_STATE_EVENT = 'agenthost://state';

export const subscribeAgentHostEvents = async (
  onEvent: (event: AgentHostEventEnvelope) => void,
): Promise<UnlistenFn> => {
  return await listen<AgentHostEventEnvelope>(AGENT_HOST_EVENT, (event) => onEvent(event.payload));
};

export const subscribeAgentHostState = async (
  onEvent: (event: AgentHostStateEvent) => void,
): Promise<UnlistenFn> => {
  return await listen<AgentHostStateEvent>(AGENT_HOST_STATE_EVENT, (event) => onEvent(event.payload));
};
