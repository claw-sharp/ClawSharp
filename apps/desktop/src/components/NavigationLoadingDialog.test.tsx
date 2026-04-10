import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NavigationLoadingDialog } from '@/components/NavigationLoadingDialog';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

const mockedUseAppStore = vi.mocked(useAppStore);

describe('NavigationLoadingDialog', () => {
  beforeEach(() => {
    mockedUseAppStore.mockReturnValue({
      ui: {
        navigationLoading: null,
      },
    } as ReturnType<typeof useAppStore>);
  });

  it('does not render when navigation is idle', () => {
    render(<NavigationLoadingDialog />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renders a blocking wait dialog while navigation data is loading', () => {
    mockedUseAppStore.mockReturnValue({
      ui: {
        navigationLoading: {
          requestId: 'nav-1',
          kind: 'thread',
          title: 'Loading thread',
          description: 'Refreshing transcript and review data for Thread One.',
        },
      },
    } as ReturnType<typeof useAppStore>);

    render(<NavigationLoadingDialog />);

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Please wait')).toBeInTheDocument();
    expect(screen.getByText('Loading thread')).toBeInTheDocument();
    expect(screen.getByText('Refreshing transcript and review data for Thread One.')).toBeInTheDocument();
  });
});
