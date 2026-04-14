import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginsPanel } from '@/features/plugins/PluginsPanel';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

vi.mock('@/components/ui/sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
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
      skills: [],
      skillsLoading: false,
      skillsError: null,
      loadPlugins: vi.fn(),
      loadSkills: vi.fn(),
      createSkill: vi.fn(),
      refreshPlugins: vi.fn(),
      installPlugin: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor: vi.fn(),
    } as ReturnType<typeof useAppStore>);
  });

  it('prompts the user to open a project when none is selected', () => {
    render(<PluginsPanel />);

    expect(screen.getByText('Plugins')).toBeInTheDocument();
    expect(screen.getByText(/Open a project to inspect discovered plugins/i)).toBeInTheDocument();
  });

  it('renders plugin details for the selected project and triggers an initial load', () => {
    const loadPlugins = vi.fn();
    const loadSkills = vi.fn();
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
          authenticated: true,
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
          mcpServers: [
            {
              name: 'linear',
              type: 'http',
              endpoint: 'https://mcp.linear.app/mcp',
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
      skills: [
        {
          name: 'release-notes',
          source: 'projectSettings',
          filePath: '/Users/hadoan/Documents/GitHub/ClawSharp/.clawsharp/skills/release-notes/SKILL.md',
          baseDirectory: '/Users/hadoan/Documents/GitHub/ClawSharp/.clawsharp/skills/release-notes',
        },
      ],
      skillsLoading: false,
      skillsError: null,
      loadPlugins,
      loadSkills,
      createSkill: vi.fn(),
      refreshPlugins: vi.fn(),
      installPlugin: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor: vi.fn(),
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    expect(loadPlugins).toHaveBeenCalledWith('proj-1');
    expect(loadSkills).toHaveBeenCalledWith('proj-1');
    expect(screen.getAllByText('reviewer')[0]).toBeInTheDocument();
    expect(screen.getByText('Review-focused helpers bundled into the app.')).toBeInTheDocument();
    expect(screen.getAllByText('Authenticated').length).toBeGreaterThan(0);
    expect(screen.getByText('linear (http)')).toBeInTheDocument();
    expect(screen.getByText('https://mcp.linear.app/mcp')).toBeInTheDocument();
    expect(screen.getByDisplayValue('/Users/hadoan/Documents/GitHub/ClawSharp')).toBeInTheDocument();
    expect(screen.getByText('Example warning')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /\$release-notes/i })).toBeInTheDocument();
  });

  it('shows an install dialog before enabling an MCP plugin', async () => {
    const installPlugin = vi.fn().mockResolvedValue(undefined);

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
          pluginId: 'linear@builtin',
          name: 'Linear',
          description: 'Manage issues and projects in Linear.',
          version: '1.0.0',
          enabled: false,
          isBundled: true,
          installPath: '/builtin/linear',
          scope: 'builtin',
          installedAt: '2026-04-01T09:00:00Z',
          lastUpdated: '2026-04-05T14:30:00Z',
          gitCommitSha: null,
          commands: [],
          agents: [],
          skills: [],
          outputStyles: [],
          hookFiles: [],
          hookEvents: [],
          validationIssues: [],
          options: [],
          mcpServers: [
            {
              name: 'linear',
              type: 'http',
              endpoint: 'https://mcp.linear.app/mcp',
            },
          ],
        },
      ],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      skills: [],
      skillsLoading: false,
      skillsError: null,
      loadPlugins: vi.fn(),
      loadSkills: vi.fn(),
      createSkill: vi.fn(),
      refreshPlugins: vi.fn(),
      installPlugin,
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor: vi.fn(),
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    fireEvent.click(screen.getByRole('button', { name: 'Enable' }));

    expect(screen.getByRole('heading', { name: 'Install Linear' })).toBeInTheDocument();
    expect(screen.getByText(/authenticate in browser/i)).toBeInTheDocument();
    expect(installPlugin).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Install Linear' }));

    await waitFor(() => {
      expect(installPlugin).toHaveBeenCalledWith('linear@builtin');
    });
  });

  it('uses generic install dialog copy for other MCP plugins', () => {
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
          pluginId: 'github@builtin',
          name: 'GitHub',
          description: 'Manage GitHub issues and PRs.',
          version: '1.0.0',
          enabled: false,
          isBundled: true,
          installPath: '/builtin/github',
          scope: 'builtin',
          installedAt: '2026-04-01T09:00:00Z',
          lastUpdated: '2026-04-05T14:30:00Z',
          gitCommitSha: null,
          commands: [],
          agents: [],
          skills: [],
          outputStyles: [],
          hookFiles: [],
          hookEvents: [],
          validationIssues: [],
          options: [],
          mcpServers: [
            {
              name: 'github',
              type: 'http',
              endpoint: 'https://api.githubcopilot.com/mcp/',
            },
          ],
        },
      ],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      skills: [],
      skillsLoading: false,
      skillsError: null,
      loadPlugins: vi.fn(),
      loadSkills: vi.fn(),
      createSkill: vi.fn(),
      refreshPlugins: vi.fn(),
      installPlugin: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor: vi.fn(),
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    fireEvent.click(screen.getByRole('button', { name: 'Enable' }));

    expect(screen.getByRole('heading', { name: 'Install GitHub' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Install GitHub' })).toBeInTheDocument();
    expect(screen.getAllByText('https://api.githubcopilot.com/mcp/').length).toBeGreaterThan(0);
  });

  it('enables non-MCP plugins without showing the install dialog', async () => {
    const setPluginEnabled = vi.fn().mockResolvedValue(undefined);

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
          enabled: false,
          isBundled: true,
          installPath: '/builtin/reviewer',
          scope: 'builtin',
          installedAt: '2026-04-01T09:00:00Z',
          lastUpdated: '2026-04-05T14:30:00Z',
          gitCommitSha: null,
          commands: [],
          agents: [],
          skills: [],
          outputStyles: [],
          hookFiles: [],
          hookEvents: [],
          validationIssues: [],
          options: [],
          mcpServers: [],
        },
      ],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      skills: [],
      skillsLoading: false,
      skillsError: null,
      loadPlugins: vi.fn(),
      loadSkills: vi.fn(),
      createSkill: vi.fn(),
      refreshPlugins: vi.fn(),
      installPlugin: vi.fn(),
      setPluginEnabled,
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor: vi.fn(),
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    fireEvent.click(screen.getByRole('button', { name: 'Enable' }));

    await waitFor(() => {
      expect(setPluginEnabled).toHaveBeenCalledWith('reviewer@builtin', true);
    });
    expect(screen.queryByRole('heading', { name: /Install/i })).not.toBeInTheDocument();
  });

  it('creates a skill from the dialog and opens it in the editor', async () => {
    const createSkill = vi.fn().mockResolvedValue({
      name: 'release-notes',
      source: 'projectSettings',
      filePath: '/Users/hadoan/Documents/GitHub/ClawSharp/.clawsharp/skills/release-notes/SKILL.md',
      baseDirectory: '/Users/hadoan/Documents/GitHub/ClawSharp/.clawsharp/skills/release-notes',
    });
    const openExternalEditor = vi.fn();

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
          commands: [],
          agents: [],
          skills: [],
          outputStyles: [],
          hookFiles: [],
          hookEvents: [],
          validationIssues: [],
          options: [],
          mcpServers: [],
        },
      ],
      pluginCatalogLoading: false,
      pluginCatalogError: null,
      skills: [],
      skillsLoading: false,
      skillsError: null,
      loadPlugins: vi.fn(),
      loadSkills: vi.fn(),
      createSkill,
      refreshPlugins: vi.fn(),
      installPlugin: vi.fn(),
      setPluginEnabled: vi.fn(),
      savePluginOptions: vi.fn(),
      deletePluginOptions: vi.fn(),
      openExternalEditor,
    } as ReturnType<typeof useAppStore>);

    render(<PluginsPanel />);

    fireEvent.click(screen.getByRole('button', { name: /add skill/i }));
    fireEvent.change(screen.getByLabelText('Skill name'), { target: { value: 'release-notes' } });
    fireEvent.change(screen.getByLabelText('Short description'), { target: { value: 'Summarize the release.' } });
    fireEvent.change(screen.getByLabelText('Guidance'), { target: { value: '1. Review merged changes.\n2. Draft concise notes.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create skill' }));

    await waitFor(() => {
      expect(createSkill).toHaveBeenCalledWith(
        'release-notes',
        'Summarize the release.',
        '1. Review merged changes.\n2. Draft concise notes.',
      );
    });
    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'file',
      path: '/Users/hadoan/Documents/GitHub/ClawSharp/.clawsharp/skills/release-notes/SKILL.md',
    });
  });
});
