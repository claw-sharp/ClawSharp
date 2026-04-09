import { useAppStore } from '@/store';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

export const SettingsDialog = () => {
  const { ui, settings, toggleSettings, updateSettings } = useAppStore();
  const selectedProvider = settings.availableProviders.find((provider) => provider.id === settings.defaultProvider);
  const modelOptions = selectedProvider?.models ?? [settings.defaultModel];

  if (!ui.settingsOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-background/80 backdrop-blur-sm" onClick={toggleSettings} />
      <div className="relative w-full max-w-lg rounded-lg border border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between px-4 py-3 border-b border-border">
          <h2 className="text-sm font-semibold text-foreground">Settings</h2>
          <button onClick={toggleSettings} className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="px-4 py-4 space-y-4 max-h-[60vh] overflow-y-auto">
          {/* Provider */}
          <SettingRow label="Default Provider">
            <select
              value={settings.defaultProvider ?? ''}
              onChange={e => {
                const provider = settings.availableProviders.find((item) => item.id === e.target.value);
                void updateSettings({ defaultProvider: e.target.value, defaultModel: provider?.defaultModel ?? settings.defaultModel });
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
              value={settings.defaultModel ?? ''}
              onChange={e => { void updateSettings({ defaultModel: e.target.value }); }}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              {modelOptions.map((model) => (
                <option key={model} value={model}>{model}</option>
              ))}
            </select>
          </SettingRow>

          <SettingRow label="Provider Endpoint">
            <span className="max-w-64 truncate text-right text-xs font-mono text-muted-foreground">{settings.providerBaseUrl}</span>
          </SettingRow>

          <SettingRow label="Transport">
            <span className="text-xs text-muted-foreground">{settings.providerTransport}</span>
          </SettingRow>

          <SettingRow label="Config Path">
            <span className="max-w-64 truncate text-right text-[10px] font-mono text-muted-foreground">{settings.configPath}</span>
          </SettingRow>

          <SettingRow label="Density">
            <div className="flex gap-1">
              {(['compact', 'comfortable', 'spacious'] as const).map(d => (
                <button
                  key={d}
                  onClick={() => { void updateSettings({ density: d }); }}
                  className={cn(
                    'rounded-md px-2 py-1 text-xs transition-colors',
                    settings.density === d ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:text-foreground'
                  )}
                >
                  {d}
                </button>
              ))}
            </div>
          </SettingRow>

          <SettingRow label="Theme">
            <div className="flex gap-1">
              {(['system', 'dark', 'light'] as const).map(theme => (
                <button
                  key={theme}
                  onClick={() => { void updateSettings({ theme }); }}
                  className={cn(
                    'rounded-md px-2 py-1 text-xs capitalize transition-colors',
                    settings.theme === theme ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:text-foreground'
                  )}
                >
                  {theme}
                </button>
              ))}
            </div>
          </SettingRow>

          <SettingRow label="Streaming Speed">
            <div className="flex gap-1">
              {(['slow', 'normal', 'fast'] as const).map(s => (
                <button
                  key={s}
                  onClick={() => { void updateSettings({ streamingSpeed: s }); }}
                  className={cn(
                    'rounded-md px-2 py-1 text-xs transition-colors',
                    settings.streamingSpeed === s ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:text-foreground'
                  )}
                >
                  {s}
                </button>
              ))}
            </div>
          </SettingRow>

          <SettingRow label="Editor Path">
            <input
              value={settings.editorPath ?? ''}
              onChange={e => { void updateSettings({ editorPath: e.target.value }); }}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground font-mono outline-none focus:border-primary/40 w-48"
            />
          </SettingRow>

          <SettingToggle label="Telemetry Enabled" checked={settings.showDiagnostics} onChange={v => { void updateSettings({ showDiagnostics: v }); }} />
          <SettingToggle label="Compact Mode" checked={settings.compactMode} onChange={v => { void updateSettings({ compactMode: v }); }} />
          <SettingToggle label="Reduced Motion" checked={settings.reducedMotion} onChange={v => { void updateSettings({ reducedMotion: v }); }} />
          <SettingToggle label="Notifications" checked={settings.notifications} onChange={v => { void updateSettings({ notifications: v }); }} />

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

        <div className="px-4 py-3 border-t border-border text-[10px] text-muted-foreground">
          Runtime settings reuse the existing ClawSharp configuration and provider resolution flow.
        </div>
      </div>
    </div>
  );
};

const SettingRow = ({ label, children }: { label: string; children: React.ReactNode }) => (
  <div className="flex items-center justify-between">
    <span className="text-xs text-secondary-foreground">{label}</span>
    {children}
  </div>
);

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
