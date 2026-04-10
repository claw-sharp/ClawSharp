import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsDialog } from '@/features/settings/SettingsDialog';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

const mockedUseAppStore = vi.mocked(useAppStore);
const updateSettings = vi.fn().mockResolvedValue(undefined);
const toggleSettings = vi.fn();

const baseStoreState = {
  ui: {
    settingsOpen: true,
  },
  settings: {
    theme: 'dark',
    density: 'comfortable',
    defaultProvider: 'anthropic',
    defaultModel: 'claude-haiku-4-5-20251001',
    fallbackModel: null,
    permissionMode: 'Default',
    providerBaseUrl: 'https://api.anthropic.com',
    providerTransport: 'AnthropicMessages',
    configPath: '~/.clawsharp/config.toml',
    settingsIssues: [],
    providerValidationWarnings: [],
    providerValidationErrors: [],
    providerCredentials: {
      hasApiKey: false,
      hasAuthToken: false,
      accountId: null,
      source: 'none',
      hasExternalCredential: true,
      externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
    },
    availableProviders: [
      {
        id: 'anthropic',
        displayName: 'Anthropic',
        defaultModel: 'claude-haiku-4-5-20251001',
        models: ['claude-haiku-4-5-20251001', 'claude-sonnet-4-5'],
        baseUrl: 'https://api.anthropic.com',
        requiresApiKey: true,
        description: 'Claude default provider selection.',
      },
      {
        id: 'codex',
        displayName: 'Codex',
        defaultModel: 'codexplan',
        models: ['codexplan', 'gpt-5.4', 'gpt-5.4-mini'],
        baseUrl: 'https://chatgpt.com/backend-api/codex',
        requiresApiKey: true,
        description: 'OpenAI Codex responses transport.',
      },
    ],
    showDiagnostics: true,
    streamingSpeed: 'normal',
    compactMode: false,
    reducedMotion: false,
    notifications: true,
    editorPath: '/usr/local/bin/code',
  },
  toggleSettings,
  updateSettings,
};

describe('SettingsDialog', () => {
  beforeEach(() => {
    updateSettings.mockReset();
    updateSettings.mockResolvedValue(undefined);
    toggleSettings.mockReset();
    mockedUseAppStore.mockReturnValue(baseStoreState as ReturnType<typeof useAppStore>);
  });

  it('uses the selected provider catalog, saves settings explicitly, saves credentials against the current runtime selection, and hides unsupported placeholder settings', () => {
    render(<SettingsDialog />);

    const [providerSelect, modelSelect] = screen.getAllByRole('combobox');

    fireEvent.change(providerSelect, { target: { value: 'codex' } });

    expect(updateSettings).not.toHaveBeenCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'codexplan',
    });
    expect(modelSelect).toHaveValue('codexplan');
    expect(screen.getByText('gpt-5.4-mini')).toBeInTheDocument();
    expect(screen.getByText(/Codex uses the Responses transport/i)).toBeInTheDocument();
    expect(screen.getByLabelText('Use Codex auth file')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }));

    expect(updateSettings).toHaveBeenCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'codexplan',
      showDiagnostics: true,
    });

    fireEvent.change(screen.getByLabelText('Codex Access Token'), { target: { value: 'codex-token' } });
    fireEvent.change(screen.getByLabelText('Codex Account ID'), { target: { value: 'acct-123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Credentials' }));

    expect(updateSettings).toHaveBeenLastCalledWith({
      providerApiKey: 'codex-token',
      providerAccountId: 'acct-123',
    });

    expect(screen.queryByText('Density')).not.toBeInTheDocument();
    expect(screen.queryByText('Theme')).not.toBeInTheDocument();
    expect(screen.queryByText('Streaming Speed')).not.toBeInTheDocument();
    expect(screen.queryByText('Compact Mode')).not.toBeInTheDocument();
  });

  it('can switch codex back to the external auth file path', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        defaultProvider: 'codex',
        defaultModel: 'codexplan',
        providerCredentials: {
          hasApiKey: true,
          hasAuthToken: false,
          accountId: 'acct-123',
          source: 'saved',
          hasExternalCredential: true,
          externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
        },
      },
    } as ReturnType<typeof useAppStore>);

    render(<SettingsDialog />);

    fireEvent.click(screen.getByLabelText('Use Codex auth file'));
    fireEvent.click(screen.getByRole('button', { name: 'Use Codex Auth File' }));

    expect(updateSettings).toHaveBeenLastCalledWith({
      useExternalProviderCredential: true,
      clearProviderApiKey: true,
      clearProviderAuthToken: false,
      clearProviderAccountId: true,
    });
  });
});
