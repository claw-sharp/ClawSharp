import { useEffect, useMemo, useRef, useState } from 'react';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import { toast } from '@/components/ui/sonner';
import type { ProviderCredentialState } from '@/types';

type GeminiCredentialMode = 'apiKey' | 'accessToken';
type CodexCredentialMode = 'external' | 'saved';

type CredentialConfig = {
  secretField: 'apiKey' | 'authToken';
  secretLabel: string;
  accountIdLabel?: string;
};

type CredentialUpdate = {
  providerApiKey?: string | null;
  providerAuthToken?: string | null;
  providerAccountId?: string | null;
  clearProviderApiKey?: boolean;
  clearProviderAuthToken?: boolean;
  clearProviderAccountId?: boolean;
};

export const SettingsDialog = () => {
  const { ui, settings, toggleSettings, updateSettings, validateProviderConfig } = useAppStore();
  const [draftProvider, setDraftProvider] = useState(settings.defaultProvider);
  const [draftModel, setDraftModel] = useState(settings.defaultModel);
  const [draftTelemetryEnabled, setDraftTelemetryEnabled] = useState(settings.showDiagnostics);
  const [apiKeyInput, setApiKeyInput] = useState('');
  const [authTokenInput, setAuthTokenInput] = useState('');
  const [accountIdInput, setAccountIdInput] = useState('');
  const [geminiCredentialMode, setGeminiCredentialMode] = useState<GeminiCredentialMode>('apiKey');
  const [codexCredentialMode, setCodexCredentialMode] = useState<CodexCredentialMode>('saved');
  const [isValidatingProvider, setIsValidatingProvider] = useState(false);
  const wasSettingsOpenRef = useRef(false);
  const hasEditedDraftRef = useRef(false);

  const resetDraftFromSettings = () => {
    setDraftProvider(settings.defaultProvider);
    setDraftModel(settings.defaultModel);
    setDraftTelemetryEnabled(settings.showDiagnostics);
    setApiKeyInput('');
    setAuthTokenInput('');
    setAccountIdInput(settings.providerCredentials.accountId ?? '');
    setGeminiCredentialMode(
      settings.providerCredentials.hasAuthToken && !settings.providerCredentials.hasApiKey
        ? 'accessToken'
        : 'apiKey',
    );
    setCodexCredentialMode(
      settings.providerCredentials.source === 'external' && settings.providerCredentials.hasExternalCredential
        ? 'external'
        : 'saved',
    );
  };

  useEffect(() => {
    if (!ui.settingsOpen) {
      wasSettingsOpenRef.current = false;
      hasEditedDraftRef.current = false;
      return;
    }

    if (wasSettingsOpenRef.current) {
      return;
    }

    wasSettingsOpenRef.current = true;
    hasEditedDraftRef.current = false;
    resetDraftFromSettings();
  }, [
    settings.defaultModel,
    settings.defaultProvider,
    settings.providerCredentials.accountId,
    settings.providerCredentials.hasExternalCredential,
    settings.providerCredentials.hasApiKey,
    settings.providerCredentials.hasAuthToken,
    settings.providerCredentials.source,
    settings.showDiagnostics,
    ui.settingsOpen,
  ]);

  useEffect(() => {
    if (!ui.settingsOpen || !wasSettingsOpenRef.current || hasEditedDraftRef.current) {
      return;
    }

    resetDraftFromSettings();
  }, [
    settings.defaultModel,
    settings.defaultProvider,
    settings.providerCredentials.accountId,
    settings.providerCredentials.hasExternalCredential,
    settings.providerCredentials.hasApiKey,
    settings.providerCredentials.hasAuthToken,
    settings.providerCredentials.source,
    settings.showDiagnostics,
    ui.settingsOpen,
  ]);

  const selectedProvider = settings.availableProviders.find((provider) => provider.id === draftProvider)
    ?? settings.availableProviders.find((provider) => provider.id === settings.defaultProvider)
    ?? settings.availableProviders[0];
  const selectedProviderId = selectedProvider?.id ?? draftProvider ?? settings.defaultProvider ?? '';
  const modelOptions = useMemo(() => {
    const nextOptions = selectedProvider?.models ?? [];
    const nextModel = draftModel || settings.defaultModel;

    if (nextModel && !nextOptions.includes(nextModel)) {
      return [nextModel, ...nextOptions];
    }

    return nextOptions.length > 0 ? nextOptions : [nextModel].filter(Boolean);
  }, [draftModel, selectedProvider, settings.defaultModel]);
  const providerDetails = selectedProvider ? getProviderDetails(selectedProvider.id) : null;
  const credentialConfig = useMemo(
    () => getCredentialConfig(selectedProviderId, geminiCredentialMode),
    [geminiCredentialMode, selectedProviderId],
  );
  const credentialUpdate = credentialConfig
    ? buildCredentialUpdate(
        selectedProviderId,
        credentialConfig,
        settings.providerCredentials,
        apiKeyInput,
        authTokenInput,
        accountIdInput,
      )
    : null;
  const hasSavedCredentials =
    settings.providerCredentials.hasApiKey ||
    settings.providerCredentials.hasAuthToken ||
    Boolean(settings.providerCredentials.accountId);
  const useExternalProviderCredential = providerIdUsesExternalCredential(selectedProviderId, codexCredentialMode);
  const hasExternalCredentialPreferenceChange =
    selectedProviderId === 'codex' &&
    useExternalProviderCredential !== (settings.providerCredentials.source === 'external');
  const hasSettingsChanges =
    draftProvider !== settings.defaultProvider ||
    draftModel !== settings.defaultModel ||
    draftTelemetryEnabled !== settings.showDiagnostics;
  const hasCredentialChanges = credentialUpdate !== null || hasExternalCredentialPreferenceChange;
  const hasPendingChanges = hasSettingsChanges || hasCredentialChanges;
  const validationRequest = {
    provider: selectedProviderId,
    model: draftModel,
    providerApiKey:
      credentialConfig?.secretField === 'apiKey' && apiKeyInput.trim().length > 0
        ? apiKeyInput.trim()
        : undefined,
    providerAuthToken:
      credentialConfig?.secretField === 'authToken' && authTokenInput.trim().length > 0
        ? authTokenInput.trim()
        : undefined,
    providerAccountId: accountIdInput.trim().length > 0 ? accountIdInput.trim() : undefined,
    useExternalProviderCredential,
    liveCheck: true,
  };

  if (!ui.settingsOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-background/80 backdrop-blur-sm" onClick={toggleSettings} />
      <div className="relative w-full max-w-lg rounded-lg border border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold text-foreground">Settings</h2>
          <button onClick={toggleSettings} className="rounded p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="max-h-[60vh] space-y-4 overflow-y-auto px-4 py-4">
          <SettingRow label="Default Provider">
            <select
              value={selectedProviderId}
              onChange={e => {
                const provider = settings.availableProviders.find((item) => item.id === e.target.value);
                const nextModel = provider?.defaultModel ?? settings.defaultModel;

                hasEditedDraftRef.current = true;
                setDraftProvider(e.target.value);
                setDraftModel(nextModel);
                setApiKeyInput('');
                setAuthTokenInput('');
                setAccountIdInput('');
                setGeminiCredentialMode('apiKey');
                setCodexCredentialMode('saved');
              }}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              {settings.availableProviders.map((provider) => (
                <option key={provider.id} value={provider.id}>{provider.displayName}</option>
              ))}
            </select>
          </SettingRow>

          <SettingRow label="Default Model">
            <select
              value={draftModel ?? settings.defaultModel ?? ''}
              onChange={e => {
                hasEditedDraftRef.current = true;
                setDraftModel(e.target.value);
              }}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              {modelOptions.map((model) => (
                <option key={model} value={model}>{model}</option>
              ))}
            </select>
          </SettingRow>

          {selectedProvider && (
            <div className="rounded-md border border-border p-3 surface-1">
              <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {selectedProvider.displayName}
              </div>
              <div className="mt-2 space-y-2 text-xs text-muted-foreground">
                <div>{selectedProvider.description}</div>
                {providerDetails && <div>{providerDetails}</div>}
              </div>
            </div>
          )}

          <CredentialPanel
            providerId={selectedProviderId}
            credentialConfig={credentialConfig}
            credentials={settings.providerCredentials}
            codexCredentialMode={codexCredentialMode}
            onCodexCredentialModeChange={setCodexCredentialMode}
            geminiCredentialMode={geminiCredentialMode}
            onGeminiCredentialModeChange={setGeminiCredentialMode}
            apiKeyInput={apiKeyInput}
            authTokenInput={authTokenInput}
            accountIdInput={accountIdInput}
            onApiKeyChange={setApiKeyInput}
            onAuthTokenChange={setAuthTokenInput}
            onAccountIdChange={setAccountIdInput}
            onClear={() => {
              void updateSettings({
                defaultProvider: draftProvider,
                defaultModel: draftModel,
                clearProviderApiKey: settings.providerCredentials.hasApiKey,
                clearProviderAuthToken: settings.providerCredentials.hasAuthToken,
                clearProviderAccountId: Boolean(settings.providerCredentials.accountId),
              });
              setApiKeyInput('');
              setAuthTokenInput('');
              setAccountIdInput('');
            }}
            onUseExternal={() => {
              void updateSettings({
                defaultProvider: draftProvider,
                defaultModel: draftModel,
                useExternalProviderCredential: true,
              });
              setApiKeyInput('');
              setAuthTokenInput('');
              setAccountIdInput('');
            }}
            canClear={hasSavedCredentials}
            onDraftInteraction={() => {
              hasEditedDraftRef.current = true;
            }}
          />

          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => {
                setIsValidatingProvider(true);
                void validateProviderConfig(validationRequest)
                  .then((result) => {
                    if (result.isValid) {
                      toast.success(`Validated ${selectedProvider?.displayName ?? selectedProviderId} successfully.`);
                      return;
                    }

                    toast.error(`Validation failed for ${selectedProvider?.displayName ?? selectedProviderId}.`);
                  })
                  .catch((error) => {
                    toast.error(error instanceof Error ? error.message : 'Failed to validate provider settings.');
                  })
                  .finally(() => {
                    setIsValidatingProvider(false);
                  });
              }}
              disabled={isValidatingProvider || !selectedProviderId}
              className={cn(
                'rounded-md border px-3 py-2 text-xs transition-colors',
                isValidatingProvider || !selectedProviderId
                  ? 'cursor-not-allowed border-border text-muted-foreground opacity-60'
                  : 'border-border text-muted-foreground hover:bg-accent hover:text-foreground',
              )}
            >
              {isValidatingProvider ? 'Validating...' : 'Validate Credentials'}
            </button>
          </div>

          <SettingRow label="Provider Endpoint">
            <span className="max-w-64 truncate text-right font-mono text-xs text-muted-foreground">{settings.providerBaseUrl}</span>
          </SettingRow>

          <SettingRow label="Transport">
            <span className="text-xs text-muted-foreground">{settings.providerTransport}</span>
          </SettingRow>

          <SettingRow label="Config Path">
            <span className="max-w-64 truncate text-right font-mono text-[10px] text-muted-foreground">{settings.configPath}</span>
          </SettingRow>

          <SettingToggle
            label="Telemetry Enabled"
            checked={draftTelemetryEnabled}
            onChange={(value) => {
              hasEditedDraftRef.current = true;
              setDraftTelemetryEnabled(value);
            }}
          />

          {/*
            Temporarily hidden until the desktop runtime persists them for real:
            density, theme, streaming speed, editor path, compact mode,
            reduced motion, and notifications.
          */}

          {settings.settingsIssues.length > 0 && (
            <div className="rounded-md border border-status-waiting/30 bg-status-waiting/10 p-3">
              <div className="text-xs font-semibold uppercase tracking-wide text-status-waiting">Settings issues</div>
              <div className="mt-2 space-y-1 text-xs text-status-waiting">
                {settings.settingsIssues.map((issue) => (
                  <div key={issue}>{issue}</div>
                ))}
              </div>
            </div>
          )}

          {(settings.providerValidationWarnings.length > 0 || settings.providerValidationErrors.length > 0) && (
            <div className="rounded-md border border-border p-3 surface-1">
              <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Provider validation</div>
              {settings.providerValidationErrors.map((error) => (
                <div key={error} className="mt-2 rounded bg-status-failed/10 px-2 py-1 text-xs text-status-failed">{error}</div>
              ))}
              {settings.providerValidationWarnings.map((warning) => (
                <div key={warning} className="mt-2 rounded bg-status-waiting/10 px-2 py-1 text-xs text-status-waiting">{warning}</div>
              ))}
            </div>
          )}
        </div>

        <div className="border-t border-border px-4 py-3">
          <div className="flex items-center justify-between gap-3">
            <div className="text-[10px] text-muted-foreground">
              Runtime settings reuse the existing ClawSharp configuration and provider resolution flow.
            </div>
            <button
              type="button"
              onClick={() => {
                if (!hasPendingChanges) {
                  return;
                }

                void updateSettings({
                  defaultProvider: draftProvider,
                  defaultModel: draftModel,
                  showDiagnostics: draftTelemetryEnabled,
                  ...(credentialUpdate ?? {}),
                  ...(hasExternalCredentialPreferenceChange
                    ? { useExternalProviderCredential }
                    : {}),
                });
                setApiKeyInput('');
                setAuthTokenInput('');
              }}
              disabled={!hasPendingChanges}
              className={cn(
                'rounded-md border px-3 py-2 text-xs transition-colors',
                hasPendingChanges
                  ? 'border-primary/40 bg-primary/10 text-foreground hover:bg-primary/15'
                  : 'cursor-not-allowed border-border text-muted-foreground opacity-60',
              )}
            >
              Save
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

const SettingRow = ({ label, children }: { label: string; children: React.ReactNode }) => (
  <label className="flex items-center justify-between cursor-pointer">
    <span className="text-xs text-secondary-foreground">{label}</span>
    {children}
  </label>
);

const CredentialPanel = ({
  providerId,
  credentialConfig,
  credentials,
  codexCredentialMode,
  onCodexCredentialModeChange,
  geminiCredentialMode,
  onGeminiCredentialModeChange,
  apiKeyInput,
  authTokenInput,
  accountIdInput,
  onApiKeyChange,
  onAuthTokenChange,
  onAccountIdChange,
  onClear,
  onUseExternal,
  canClear,
  onDraftInteraction,
}: {
  providerId: string;
  credentialConfig: CredentialConfig | null;
  credentials: ProviderCredentialState;
  codexCredentialMode: CodexCredentialMode;
  onCodexCredentialModeChange: (mode: CodexCredentialMode) => void;
  geminiCredentialMode: GeminiCredentialMode;
  onGeminiCredentialModeChange: (mode: GeminiCredentialMode) => void;
  apiKeyInput: string;
  authTokenInput: string;
  accountIdInput: string;
  onApiKeyChange: (value: string) => void;
  onAuthTokenChange: (value: string) => void;
  onAccountIdChange: (value: string) => void;
  onClear: () => void;
  onUseExternal: () => void;
  canClear: boolean;
  onDraftInteraction: () => void;
}) => {
  if (!credentialConfig) {
    return (
      <div className="rounded-md border border-border p-3 surface-1">
        <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Credentials</div>
        <div className="mt-2 text-xs text-muted-foreground">
          This provider uses the local runtime endpoint directly and does not require a saved cloud credential.
        </div>
      </div>
    );
  }

  const secretValue = credentialConfig.secretField === 'apiKey' ? apiKeyInput : authTokenInput;
  const useCodexExternal = providerId === 'codex' && codexCredentialMode === 'external';
  const canUseCodexExternal = credentials.hasExternalCredential;

  return (
    <div className="rounded-md border border-border p-3 surface-1">
      <div className="flex items-center justify-between gap-3">
        <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Credentials</div>
        <div className="text-[10px] text-muted-foreground">{formatCredentialStatus(providerId, credentials)}</div>
      </div>

      {providerId === 'codex' && (
        <div className="mt-3 space-y-2">
          <label className="flex items-start gap-2 text-xs text-secondary-foreground">
            <input
              aria-label="Use Codex auth file"
              type="radio"
              name="codex-credential-mode"
              checked={codexCredentialMode === 'external'}
              onChange={() => {
                onDraftInteraction();
                onCodexCredentialModeChange('external');
              }}
              className="mt-0.5"
            />
            <span>
              Use Codex auth file
              <span className="block text-[10px] text-muted-foreground">
                {credentials.externalCredentialPath ?? '%USERPROFILE%\\.codex\\auth.json'}
              </span>
            </span>
          </label>
          <label className="flex items-start gap-2 text-xs text-secondary-foreground">
            <input
              aria-label="Use saved access token and account ID"
              type="radio"
              name="codex-credential-mode"
              checked={codexCredentialMode === 'saved'}
              onChange={() => {
                onDraftInteraction();
                onCodexCredentialModeChange('saved');
              }}
              className="mt-0.5"
            />
            <span>
              Use saved access token and account ID
            </span>
          </label>
          {!credentials.hasExternalCredential && (
            <div className="text-[10px] text-muted-foreground">
              No Codex auth file was found at the local default path.
            </div>
          )}
        </div>
      )}

      {providerId === 'gemini' && (
        <div className="mt-3">
          <label className="block space-y-1">
            <span className="text-xs text-secondary-foreground">Gemini Credential Type</span>
            <select
              aria-label="Gemini credential type"
              value={geminiCredentialMode}
              onChange={event => {
                onDraftInteraction();
                onGeminiCredentialModeChange(event.target.value as GeminiCredentialMode);
              }}
              className="w-full rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              <option value="apiKey">API key</option>
              <option value="accessToken">Access token</option>
            </select>
          </label>
        </div>
      )}

      {useCodexExternal ? (
        <div className="mt-3 space-y-3">
          <div className="text-xs text-muted-foreground">
            ClawSharp will read the token and account metadata from the existing Codex auth file instead of saving a token in `settings.json`.
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={onUseExternal}
              disabled={!canUseCodexExternal || credentials.source === 'external'}
              className={cn(
                'rounded-md border px-3 py-2 text-xs transition-colors',
                canUseCodexExternal && credentials.source !== 'external'
                  ? 'border-primary/40 bg-primary/10 text-foreground hover:bg-primary/15'
                  : 'cursor-not-allowed border-border text-muted-foreground opacity-60',
              )}
            >
              Use Codex Auth File
            </button>
          </div>
        </div>
      ) : (
      <div className="mt-3 space-y-3">
        <label className="block space-y-1">
          <span className="text-xs text-secondary-foreground">{credentialConfig.secretLabel}</span>
          <input
            aria-label={credentialConfig.secretLabel}
            type="password"
            value={secretValue}
            onChange={event => {
              const nextValue = event.target.value;
              onDraftInteraction();
              if (credentialConfig.secretField === 'apiKey') {
                onApiKeyChange(nextValue);
              } else {
                onAuthTokenChange(nextValue);
              }
            }}
            className="w-full rounded-md border border-border bg-input px-2 py-2 text-xs text-foreground outline-none focus:border-primary/40"
            placeholder={`Enter ${credentialConfig.secretLabel.toLowerCase()}`}
            autoComplete="off"
          />
        </label>

        {credentialConfig.accountIdLabel && (
          <label className="block space-y-1">
            <span className="text-xs text-secondary-foreground">{credentialConfig.accountIdLabel}</span>
            <input
            aria-label={credentialConfig.accountIdLabel}
            type="text"
            value={accountIdInput}
            onChange={event => {
              onDraftInteraction();
              onAccountIdChange(event.target.value);
            }}
            className="w-full rounded-md border border-border bg-input px-2 py-2 text-xs text-foreground outline-none focus:border-primary/40"
            placeholder={`Enter ${credentialConfig.accountIdLabel.toLowerCase()}`}
            autoComplete="off"
            />
          </label>
        )}

        <div className="flex gap-2">
          <button
            type="button"
            onClick={onClear}
            disabled={!canClear}
            className={cn(
              'rounded-md border px-3 py-2 text-xs transition-colors',
              canClear
                ? 'border-border text-muted-foreground hover:bg-accent hover:text-foreground'
                : 'cursor-not-allowed border-border text-muted-foreground opacity-60',
            )}
          >
            Clear Saved Credentials
          </button>
        </div>
      </div>
      )}
    </div>
  );
};

function buildCredentialUpdate(
  providerId: string,
  credentialConfig: CredentialConfig,
  credentials: ProviderCredentialState,
  apiKeyInput: string,
  authTokenInput: string,
  accountIdInput: string,
): CredentialUpdate | null {
  const trimmedApiKey = apiKeyInput.trim();
  const trimmedAuthToken = authTokenInput.trim();
  const trimmedAccountId = accountIdInput.trim();
  const next: CredentialUpdate = {};

  if (credentialConfig.secretField === 'apiKey' && trimmedApiKey) {
    next.providerApiKey = trimmedApiKey;
    if (providerId === 'gemini' && credentials.hasAuthToken) {
      next.clearProviderAuthToken = true;
    }
  }

  if (credentialConfig.secretField === 'authToken' && trimmedAuthToken) {
    next.providerAuthToken = trimmedAuthToken;
    if (providerId === 'gemini' && credentials.hasApiKey) {
      next.clearProviderApiKey = true;
    }
  }

  if (credentialConfig.accountIdLabel) {
    const savedAccountId = credentials.accountId ?? '';
    if (trimmedAccountId && trimmedAccountId !== savedAccountId) {
      next.providerAccountId = trimmedAccountId;
    } else if (!trimmedAccountId && savedAccountId) {
      next.clearProviderAccountId = true;
    }
  }

  return Object.keys(next).length > 0 ? next : null;
}

function getCredentialConfig(providerId: string, geminiCredentialMode: GeminiCredentialMode): CredentialConfig | null {
  switch (providerId) {
    case 'anthropic':
      return { secretField: 'apiKey', secretLabel: 'Anthropic API Key' };
    case 'openai':
      return { secretField: 'apiKey', secretLabel: 'OpenAI API Key' };
    case 'codex':
      return {
        secretField: 'apiKey',
        secretLabel: 'Codex Access Token',
        accountIdLabel: 'Codex Account ID',
      };
    case 'gemini':
      return geminiCredentialMode === 'accessToken'
        ? { secretField: 'authToken', secretLabel: 'Gemini Access Token' }
        : { secretField: 'apiKey', secretLabel: 'Gemini API Key' };
    case 'github':
      return { secretField: 'authToken', secretLabel: 'GitHub Token' };
    default:
      return null;
  }
}

function formatCredentialStatus(providerId: string, credentials: ProviderCredentialState): string {
  switch (providerId) {
    case 'anthropic':
    case 'openai':
      return credentials.hasApiKey ? 'API key saved' : 'No saved API key';
    case 'codex':
      if (credentials.source === 'external') {
        return 'Using Codex auth file';
      }

      if (credentials.hasApiKey && credentials.accountId) {
        return 'Token and account ID saved';
      }

      if (credentials.hasApiKey) {
        return 'Token saved';
      }

      return credentials.accountId ? 'Account ID saved' : 'No saved token';
    case 'gemini':
      if (credentials.hasApiKey) {
        return 'API key saved';
      }

      if (credentials.hasAuthToken) {
        return 'Access token saved';
      }

      return 'No saved credential';
    case 'github':
      return credentials.hasAuthToken ? 'Token saved' : 'No saved token';
    default:
      return 'No saved credential';
  }
}

function getProviderDetails(providerId: string): string {
  switch (providerId) {
    case 'anthropic':
      return 'ClawSharp uses ANTHROPIC_API_KEY first, then falls back to Claude auth tokens from the existing local login state.';
    case 'openai':
      return 'Set CLAUDE_CODE_USE_OPENAI=1 with OPENAI_API_KEY, OPENAI_MODEL, and optionally OPENAI_BASE_URL for compatible gateways.';
    case 'codex':
      return 'Codex uses the Responses transport and reads CODEX_API_KEY, CODEX_ACCOUNT_ID or CHATGPT_ACCOUNT_ID, or auth.json from CODEX_HOME or %USERPROFILE%\\.codex.';
    case 'gemini':
      return 'Gemini runs through the OpenAI-compatible transport and reads GEMINI_API_KEY, GOOGLE_API_KEY, or GEMINI_ACCESS_TOKEN plus GEMINI_MODEL.';
    case 'github':
      return 'GitHub Models reads GITHUB_TOKEN or GH_TOKEN and maps GitHub-hosted models through the OpenAI-compatible transport.';
    case 'ollama':
      return 'Ollama uses a local OpenAI-compatible endpoint at http://localhost:11434/v1 and does not require a cloud API key.';
    default:
      return 'Provider credentials and transport are resolved by the runtime configuration for the active workspace.';
  }
}

function providerIdUsesExternalCredential(
  providerId: string,
  codexCredentialMode: CodexCredentialMode,
): boolean {
  return providerId === 'codex' && codexCredentialMode === 'external';
}

const SettingToggle = ({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) => (
  <div className="flex items-center justify-between">
    <span className="text-xs text-secondary-foreground">{label}</span>
    <button
      onClick={() => onChange(!checked)}
      className={cn(
        'relative h-5 w-9 rounded-full transition-colors',
        checked ? 'bg-primary' : 'bg-muted'
      )}
    >
      <span className={cn(
        'absolute top-0.5 left-0.5 h-4 w-4 rounded-full bg-foreground transition-transform',
        checked && 'translate-x-4',
        !checked && 'bg-muted-foreground'
      )} />
    </button>
  </div>
);
