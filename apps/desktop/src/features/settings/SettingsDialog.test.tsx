import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsDialog } from '@/features/settings/SettingsDialog';
import { useAppStore } from '@/store';

vi.mock('@/store', () => ({
  useAppStore: vi.fn(),
}));

const mockedUseAppStore = vi.mocked(useAppStore);
const updateSettings = vi.fn().mockResolvedValue(undefined);
const validateProviderConfig = vi.fn().mockResolvedValue({ isValid: true, warnings: [], errors: [] });
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
    hasAnyConfiguredProviderCredential: false,
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
        defaultModel: 'gpt-5.4',
        models: ['gpt-5.4', 'gpt-5.4-mini', 'codexplan'],
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
  validateProviderConfig,
};

describe('SettingsDialog', () => {
  beforeEach(() => {
    updateSettings.mockReset();
    updateSettings.mockResolvedValue(undefined);
    validateProviderConfig.mockReset();
    validateProviderConfig.mockResolvedValue({ isValid: true, warnings: [], errors: [] });
    toggleSettings.mockReset();
    mockedUseAppStore.mockReturnValue(baseStoreState as ReturnType<typeof useAppStore>);
  });

  it('uses the selected provider catalog, saves provider settings and credentials together, and hides unsupported placeholder settings', () => {
    render(<SettingsDialog />);

    const [providerSelect, modelSelect] = screen.getAllByRole('combobox');

    fireEvent.change(providerSelect, { target: { value: 'codex' } });

    expect(updateSettings).not.toHaveBeenCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'gpt-5.4',
    });
    expect(modelSelect).toHaveValue('gpt-5.4');
    expect(screen.getByText('gpt-5.4-mini')).toBeInTheDocument();
    expect(screen.getByText(/Codex uses the Responses transport/i)).toBeInTheDocument();
    expect(screen.getByLabelText('Use Codex auth file')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Codex Access Token'), { target: { value: 'codex-token' } });
    fireEvent.change(screen.getByLabelText('Codex Account ID'), { target: { value: 'acct-123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(updateSettings).toHaveBeenCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'gpt-5.4',
      theme: 'dark',
      showDiagnostics: true,
      providerApiKey: 'codex-token',
      providerAccountId: 'acct-123',
    });

    expect(screen.queryByText('Density')).not.toBeInTheDocument();
    expect(screen.queryByText('Streaming Speed')).not.toBeInTheDocument();
    expect(screen.queryByText('Compact Mode')).not.toBeInTheDocument();
  });

  it('can switch codex back to the external auth file path', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        defaultProvider: 'codex',
        defaultModel: 'gpt-5.4',
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
      defaultProvider: 'codex',
      defaultModel: 'gpt-5.4',
      useExternalProviderCredential: true,
    });
  });

  it('can switch codex back to saved credentials without clearing the saved token', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        defaultProvider: 'codex',
        defaultModel: 'gpt-5.4',
        providerCredentials: {
          hasApiKey: true,
          hasAuthToken: false,
          accountId: 'acct-123',
          source: 'external',
          hasExternalCredential: true,
          externalCredentialPath: 'C:\\Users\\hadoa\\.codex\\auth.json',
        },
      },
    } as ReturnType<typeof useAppStore>);

    render(<SettingsDialog />);

    fireEvent.click(screen.getByLabelText('Use saved access token and account ID'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(updateSettings).toHaveBeenLastCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'gpt-5.4',
      theme: 'dark',
      showDiagnostics: true,
      useExternalProviderCredential: false,
    });
  });

  it('includes the pending provider selection when saving the combined draft', () => {
    render(<SettingsDialog />);

    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: 'codex' } });
    fireEvent.change(screen.getByLabelText('Codex Access Token'), { target: { value: 'codex-token' } });
    fireEvent.change(screen.getByLabelText('Codex Account ID'), { target: { value: 'acct-123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(updateSettings).toHaveBeenLastCalledWith({
      defaultProvider: 'codex',
      defaultModel: 'gpt-5.4',
      theme: 'dark',
      showDiagnostics: true,
      providerApiKey: 'codex-token',
      providerAccountId: 'acct-123',
    });
  });

  it('validates the draft provider settings against the live validation action', async () => {
    render(<SettingsDialog />);

    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: 'codex' } });
    fireEvent.change(screen.getByLabelText('Codex Access Token'), { target: { value: 'codex-token' } });
    fireEvent.change(screen.getByLabelText('Codex Account ID'), { target: { value: 'acct-123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Validate Credentials' }));

    await waitFor(() => {
      expect(validateProviderConfig).toHaveBeenCalledWith({
        provider: 'codex',
        model: 'gpt-5.4',
        providerApiKey: 'codex-token',
        providerAuthToken: undefined,
        providerAccountId: 'acct-123',
        useExternalProviderCredential: false,
        liveCheck: true,
      });
    });
  });

  it('keeps the draft provider selection while the dialog is open during validation updates', () => {
    const { rerender } = render(<SettingsDialog />);

    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: 'codex' } });
    expect(screen.getAllByRole('combobox')[0]).toHaveValue('codex');

    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        providerValidationWarnings: ['Live validation is unavailable.'],
      },
    } as ReturnType<typeof useAppStore>);

    rerender(<SettingsDialog />);

    expect(screen.getAllByRole('combobox')[0]).toHaveValue('codex');
  });

  it('syncs the provider selection when settings refresh after the dialog opens and the user has not edited anything', () => {
    const { rerender } = render(<SettingsDialog />);

    expect(screen.getAllByRole('combobox')[0]).toHaveValue('anthropic');

    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        defaultProvider: 'codex',
        defaultModel: 'gpt-5.4',
        providerBaseUrl: 'https://chatgpt.com/backend-api/codex',
        providerTransport: 'CodexResponses',
        providerCredentials: {
          ...baseStoreState.settings.providerCredentials,
          source: 'external',
        },
      },
    } as ReturnType<typeof useAppStore>);

    rerender(<SettingsDialog />);

    expect(screen.getAllByRole('combobox')[0]).toHaveValue('codex');
    expect(screen.getAllByRole('combobox')[1]).toHaveValue('gpt-5.4');
  });

  it('shows a disabled placeholder when the provider catalog is unavailable', () => {
    mockedUseAppStore.mockReturnValue({
      ...baseStoreState,
      settings: {
        ...baseStoreState.settings,
        availableProviders: [],
      },
    } as ReturnType<typeof useAppStore>);

    render(<SettingsDialog />);

    const providerSelect = screen.getAllByRole('combobox')[0];
    expect(providerSelect).toBeDisabled();
    expect(screen.getByText('Provider catalog is unavailable right now. Reopen settings after AgentHost finishes loading.')).toBeInTheDocument();
  });
});
