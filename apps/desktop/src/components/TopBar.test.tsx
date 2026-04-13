import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TopBar } from '@/components/TopBar';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

vi.mock('next-themes', () => ({
  useTheme: () => ({
    resolvedTheme: 'dark',
  }),
}));

vi.mock('@/components/ui/dropdown-menu', () => ({
  DropdownMenu: ({ children }: { children: unknown }) => <div>{children as never}</div>,
  DropdownMenuTrigger: ({ children }: { children: unknown }) => <>{children as never}</>,
  DropdownMenuContent: ({ children }: { children: unknown }) => <div>{children as never}</div>,
  DropdownMenuItem: ({
    children,
    onClick,
    className,
  }: {
    children: unknown;
    onClick?: () => void;
    className?: string;
  }) => (
    <button type="button" className={className} onClick={onClick}>
      {children as never}
    </button>
  ),
}));

const mockedUseAppStore = vi.mocked(useAppStore);

const baseStoreState = {
  selectedProjectId: '',
  projects: [],
  inboxItems: [],
  run: {
    isRunning: false,
    progressLabel: '',
  },
  connection: {
    isBootstrapping: false,
    statusLabel: 'Connected',
  },
  openProjectPicker: vi.fn(),
  toggleSettings: vi.fn(),
  toggleCommandPalette: vi.fn(),
  setActiveView: vi.fn(),
  openExternalEditor: vi.fn(),
  ui: {
    activeView: 'threads',
  },
};

describe('TopBar', () => {
  beforeEach(() => {
    mockedUseAppStore.mockReturnValue(baseStoreState as ReturnType<typeof useAppStore>);
  });

  it('opens the selected project in VS Code from the header action', () => {
    const openExternalEditor = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          branch: 'main',
          activeThreadCount: 1,
          lastUpdated: '2026-04-13T18:45:00Z',
        },
      ],
      openExternalEditor,
    } as ReturnType<typeof useAppStore>);

    render(<TopBar />);

    fireEvent.click(screen.getByRole('button', { name: /open vs/i }));

    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'project',
      projectId: 'proj-1',
      editorCommand: '__vscode__',
    });
  });

  it('shows alternate project open targets in the header menu', async () => {
    const openExternalEditor = vi.fn();
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      selectedProjectId: 'proj-1',
      projects: [
        {
          id: 'proj-1',
          name: 'ClawSharp',
          path: '/repo',
          branch: 'main',
          activeThreadCount: 1,
          lastUpdated: '2026-04-13T18:45:00Z',
        },
      ],
      openExternalEditor,
    } as ReturnType<typeof useAppStore>);

    render(<TopBar />);

    fireEvent.click(await screen.findByText('Finder'));

    expect(openExternalEditor).toHaveBeenCalledWith({
      kind: 'project',
      projectId: 'proj-1',
      editorCommand: '__finder__',
    });
    expect(await screen.findByText('VS Code')).toBeInTheDocument();
    expect(await screen.findByText('Antigravity')).toBeInTheDocument();
    expect(await screen.findByText('Terminal')).toBeInTheDocument();
  });
});
