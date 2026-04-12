import { describe, expect, it } from 'vitest';
import { BrowserAgentHostClient } from '@/lib/browserAgentHostClient';

describe('BrowserAgentHostClient plugin mocks', () => {
  it('preserves plugin enabled state when saving options', async () => {
    const client = new BrowserAgentHostClient();
    const projectId = 'proj-1';

    await client.setPluginEnabled({
      projectId,
      pluginId: 'settings@builtin',
      enabled: false,
    });

    const response = await client.savePluginOptions({
      projectId,
      pluginId: 'settings@builtin',
      values: {
        preferredScope: 'local',
      },
    });

    const plugin = response.plugins.find((entry) => entry.pluginId === 'settings@builtin');
    expect(plugin?.enabled).toBe(false);
    expect(plugin?.options.find((option) => option.key === 'preferredScope')?.value).toBe('local');
  });

  it('throws when a plugin mutation targets an unknown plugin', async () => {
    const client = new BrowserAgentHostClient();

    await expect(client.setPluginEnabled({
      projectId: 'proj-1',
      pluginId: 'missing@builtin',
      enabled: true,
    })).rejects.toThrow("Plugin 'missing@builtin' was not found in browser preview mode.");
  });
});
