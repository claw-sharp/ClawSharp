import { useAppStore } from '@/store';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

export const SettingsDialog = () => {
  const { ui, settings, toggleSettings, updateSettings } = useAppStore();

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
              value={settings.defaultProvider}
              onChange={e => updateSettings({ defaultProvider: e.target.value })}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              <option value="anthropic">Anthropic</option>
              <option value="openai">OpenAI</option>
              <option value="local">Local</option>
            </select>
          </SettingRow>

          <SettingRow label="Default Model">
            <select
              value={settings.defaultModel}
              onChange={e => updateSettings({ defaultModel: e.target.value })}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-primary/40"
            >
              <option value="claude-4-sonnet">claude-4-sonnet</option>
              <option value="claude-4-opus">claude-4-opus</option>
              <option value="o3">o3</option>
              <option value="gpt-4.1">gpt-4.1</option>
            </select>
          </SettingRow>

          <SettingRow label="Density">
            <div className="flex gap-1">
              {(['compact', 'comfortable', 'spacious'] as const).map(d => (
                <button
                  key={d}
                  onClick={() => updateSettings({ density: d })}
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

          <SettingRow label="Streaming Speed">
            <div className="flex gap-1">
              {(['slow', 'normal', 'fast'] as const).map(s => (
                <button
                  key={s}
                  onClick={() => updateSettings({ streamingSpeed: s })}
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
              value={settings.editorPath}
              onChange={e => updateSettings({ editorPath: e.target.value })}
              className="rounded-md border border-border bg-input px-2 py-1 text-xs text-foreground font-mono outline-none focus:border-primary/40 w-48"
            />
          </SettingRow>

          <SettingToggle label="Show Diagnostics" checked={settings.showDiagnostics} onChange={v => updateSettings({ showDiagnostics: v })} />
          <SettingToggle label="Compact Mode" checked={settings.compactMode} onChange={v => updateSettings({ compactMode: v })} />
          <SettingToggle label="Reduced Motion" checked={settings.reducedMotion} onChange={v => updateSettings({ reducedMotion: v })} />
          <SettingToggle label="Notifications" checked={settings.notifications} onChange={v => updateSettings({ notifications: v })} />
        </div>

        <div className="px-4 py-3 border-t border-border text-[10px] text-muted-foreground">
          Settings are stored locally · ⌘K to open command palette
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
