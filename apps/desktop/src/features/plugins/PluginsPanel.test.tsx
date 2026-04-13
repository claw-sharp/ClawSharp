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
    expect(screen.getByDisplayValue('/Users/hadoan/Documents/GitHub/ClawSharp')).toBeInTheDocument();
    expect(screen.getByText('Example warning')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /\$release-notes/i })).toBeInTheDocument();
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
