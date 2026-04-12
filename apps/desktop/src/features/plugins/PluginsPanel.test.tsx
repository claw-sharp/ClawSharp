import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginsPanel } from '@/features/plugins/PluginsPanel';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

const mockedUseAppStore = vi.mocked(useAppStore);

describe('PluginsPanel', () => {
  beforeEach(() => {
    mockedUseAppStore.mockReturnValue({
      projects: [],
      selectedProjectId: '',
      pluginCatalog: [],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      loadPlugins: vi.fn(),
      refreshPlugins: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
    } as ReturnType<typeof useAppStore>);
  });

  it('prompts the user to open a project when none is selected', () => {
    render(<PluginsPanel />);

    expect(screen.getByText('Plugins')).toBeInTheDocument();
    expect(screen.getByText(/Open a project to inspect discovered plugins/i)).toBeInTheDocument();
  });

  it('renders plugin details for the selected project and triggers an initial load', () => {
    const loadPlugins = vi.fn();
    mockedUseAppStore.mockReturnValue({
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/Users/hadoan/Documents/GitHub/ClawSharp',
          activeThreadCount: 2,
          lastUpdated: '2026-04-12T10:00:00Z',
        },
      ],
      selectedProjectId: 'proj-1',
      pluginCatalog: [
        {
          pluginId: 'reviewer@builtin',
          name: 'reviewer',
          description: 'Review-focused helpers bundled into the app.',
          version: '1.0.0',
          enabled: true,
          isBundled: true,
          installPath: '/builtin/reviewer',
          scope: 'builtin',
          installedAt: '2026-04-01T09:00:00Z',
          lastUpdated: '2026-04-05T14:30:00Z',
          gitCommitSha: null,
          commands: ['review'],
          agents: ['reviewer'],
          skills: ['reviewer-skill'],
          outputStyles: [],
          hookFiles: ['hooks/reviewer-hooks.json'],
          hookEvents: ['PreToolUse'],
          validationIssues: [
            {
              path: 'plugin.json',
              message: 'Example warning',
              isWarning: true,
            },
          ],
          options: [
            {
              key: 'workspaceRoot',
              type: 'directory',
              title: 'Workspace root',
              description: 'Directory to review',
              required: true,
              multiple: false,
              sensitive: false,
              hasValue: true,
              value: '/Users/hadoan/Documents/GitHub/ClawSharp',
              defaultValue: null,
              min: null,
              max: null,
            },
          ],
        },
      ],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      loadPlugins,
      refreshPlugins: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    expect(loadPlugins).toHaveBeenCalledWith('proj-1');
    expect(screen.getAllByText('reviewer')[0]).toBeInTheDocument();
    expect(screen.getByText('Review-focused helpers bundled into the app.')).toBeInTheDocument();
    expect(screen.getByDisplayValue('/Users/hadoan/Documents/GitHub/ClawSharp')).toBeInTheDocument();
    expect(screen.getByText('Example warning')).toBeInTheDocument();
  });
});
