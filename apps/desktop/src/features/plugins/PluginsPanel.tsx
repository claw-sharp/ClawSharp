import { type ReactNode, useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, Plug, RefreshCw, Settings2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import type { Plugin, PluginOption } from '@/types';

type FilterMode = 'all' | 'enabled' | 'disabled' | 'issues';
type DraftValues = Record<string, string | boolean>;

const filterModes: Array<{ id: FilterMode; label: string }> = [
  { id: 'all', label: 'All' },
  { id: 'enabled', label: 'Enabled' },
  { id: 'disabled', label: 'Disabled' },
  { id: 'issues', label: 'Issues' },
];

export const PluginsPanel = () => {
  const {
    projects,
    selectedProjectId,
    pluginCatalog,
    pluginCatalogLoading,
    pluginCatalogError,
    loadPlugins,
    refreshPlugins,
    setPluginEnabled,
    savePluginOptions,
    deletePluginOptions,
  } = useAppStore();
  const [selectedPluginId, setSelectedPluginId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [filterMode, setFilterMode] = useState<FilterMode>('all');
  const [draftValues, setDraftValues] = useState<DraftValues>({});
  const [touchedOptions, setTouchedOptions] = useState<string[]>([]);

  const selectedProject = projects.find((project) => project.id === selectedProjectId) ?? null;

  useEffect(() => {
    if (selectedProjectId) {
      void loadPlugins(selectedProjectId);
    }
  }, [loadPlugins, selectedProjectId]);

  const filteredPlugins = useMemo(() => {
    return pluginCatalog.filter((plugin) => {
      const matchesSearch = [plugin.name, plugin.description, plugin.pluginId]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(search.trim().toLowerCase()));
      if (!matchesSearch) {
        return false;
      }

      switch (filterMode) {
        case 'enabled':
          return plugin.enabled;
        case 'disabled':
          return !plugin.enabled;
        case 'issues':
          return plugin.validationIssues.length > 0;
        default:
          return true;
      }
    });
  }, [filterMode, pluginCatalog, search]);

  const selectedPlugin = filteredPlugins.find((plugin) => plugin.pluginId === selectedPluginId)
    ?? pluginCatalog.find((plugin) => plugin.pluginId === selectedPluginId)
    ?? filteredPlugins[0]
    ?? pluginCatalog[0]
    ?? null;

  useEffect(() => {
    if (!selectedPlugin) {
      setSelectedPluginId(null);
      setDraftValues({});
      setTouchedOptions([]);
      return;
    }

    setSelectedPluginId(selectedPlugin.pluginId);
    setDraftValues(buildInitialDraftValues(selectedPlugin));
    setTouchedOptions([]);
  }, [selectedPlugin]);

  const hasDraftChanges = touchedOptions.length > 0;
  const enabledCount = pluginCatalog.filter((plugin) => plugin.enabled).length;
  const issueCount = pluginCatalog.reduce((count, plugin) => count + plugin.validationIssues.length, 0);

  if (!selectedProjectId || !selectedProject) {
    return (
      <div className="flex h-full items-center justify-center p-6">
        <div className="max-w-md rounded-xl border border-border surface-1 p-6 text-center">
          <Plug className="mx-auto h-8 w-8 text-muted-foreground" />
          <h2 className="mt-4 text-lg font-semibold text-foreground">Plugins</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Open a project to inspect discovered plugins, toggle them on or off, and edit plugin options.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4 p-4 lg:p-6">
      <div className="rounded-2xl border border-border bg-card p-4 shadow-sm">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <div className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">Plugins</div>
            <div className="mt-2 text-2xl font-semibold text-foreground">{selectedProject.name}</div>
            <div className="mt-1 text-sm text-muted-foreground">{selectedProject.path}</div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill label={`${pluginCatalog.length} total`} tone="neutral" />
            <StatusPill label={`${enabledCount} enabled`} tone="success" />
            <StatusPill label={`${issueCount} issues`} tone={issueCount > 0 ? 'warning' : 'neutral'} />
            <button
              onClick={() => void refreshPlugins()}
              disabled={pluginCatalogLoading}
              className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-60"
            >
              <RefreshCw className={cn('h-4 w-4', pluginCatalogLoading && 'animate-spin')} />
              Refresh
            </button>
          </div>
        </div>
        {pluginCatalogError && (
          <div className="mt-4 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
            {pluginCatalogError}
          </div>
        )}
      </div>

      <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <div className="flex min-h-0 flex-col rounded-2xl border border-border bg-card shadow-sm">
          <div className="border-b border-border p-4">
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search plugins..."
              className="w-full rounded-md border border-border bg-input px-3 py-2 text-sm text-foreground outline-none focus:border-primary/40"
            />
            <div className="mt-3 flex flex-wrap gap-2">
              {filterModes.map((filter) => (
                <button
                  key={filter.id}
                  onClick={() => setFilterMode(filter.id)}
                  className={cn(
                    'rounded-full px-3 py-1 text-xs font-medium transition-colors',
                    filterMode === filter.id
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-accent text-muted-foreground hover:text-foreground'
                  )}
                >
                  {filter.label}
                </button>
              ))}
            </div>
          </div>
          <div className="flex-1 overflow-y-auto p-3">
            {filteredPlugins.length === 0 ? (
              <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                No plugins match the current filters.
              </div>
            ) : (
              <div className="space-y-2">
                {filteredPlugins.map((plugin) => (
                  <button
                    key={plugin.pluginId}
                    onClick={() => setSelectedPluginId(plugin.pluginId)}
                    className={cn(
                      'w-full rounded-xl border p-3 text-left transition-colors',
                      plugin.pluginId === selectedPlugin?.pluginId
                        ? 'border-primary/40 bg-primary/5'
                        : 'border-border hover:bg-accent/40'
                    )}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-sm font-semibold text-foreground">{plugin.name}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{plugin.pluginId}</div>
                      </div>
                      <StatusPill label={plugin.enabled ? 'Enabled' : 'Disabled'} tone={plugin.enabled ? 'success' : 'neutral'} />
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <StatusPill label={plugin.scope} tone="neutral" />
                      {plugin.version && <StatusPill label={`v${plugin.version}`} tone="neutral" />}
                      {plugin.validationIssues.length > 0 && (
                        <StatusPill label={`${plugin.validationIssues.length} issue${plugin.validationIssues.length === 1 ? '' : 's'}`} tone="warning" />
                      )}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="min-h-0 overflow-y-auto rounded-2xl border border-border bg-card shadow-sm">
          {!selectedPlugin ? (
            <div className="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">
              No plugin selected.
            </div>
          ) : (
            <div className="space-y-6 p-5">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-2xl font-semibold text-foreground">{selectedPlugin.name}</h2>
                    <StatusPill label={selectedPlugin.enabled ? 'Enabled' : 'Disabled'} tone={selectedPlugin.enabled ? 'success' : 'neutral'} />
                    <StatusPill label={selectedPlugin.scope} tone="neutral" />
                    {selectedPlugin.version && <StatusPill label={`v${selectedPlugin.version}`} tone="neutral" />}
                  </div>
                  <div className="mt-2 text-sm text-muted-foreground">{selectedPlugin.description || 'No plugin description available.'}</div>
                </div>
                <div className="flex flex-wrap gap-2">
                  <button
                    onClick={() => void setPluginEnabled(selectedPlugin.pluginId, !selectedPlugin.enabled)}
                    disabled={pluginCatalogLoading}
                    className="rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground transition-colors hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {selectedPlugin.enabled ? 'Disable' : 'Enable'}
                  </button>
                  {selectedPlugin.options.some((option) => option.hasValue) && (
                    <button
                      onClick={() => void deletePluginOptions(selectedPlugin.pluginId)}
                      disabled={pluginCatalogLoading}
                      className="rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-60"
                    >
                      Reset config
                    </button>
                  )}
                </div>
              </div>

              <Section title="Components">
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                  <MetricCard label="Commands" values={selectedPlugin.commands} />
                  <MetricCard label="Agents" values={selectedPlugin.agents} />
                  <MetricCard label="Skills" values={selectedPlugin.skills} />
                  <MetricCard label="Hook events" values={selectedPlugin.hookEvents} />
                  <MetricCard label="Hook files" values={selectedPlugin.hookFiles} />
                  <MetricCard label="Output styles" values={selectedPlugin.outputStyles} />
                </div>
              </Section>

              <Section title="Configuration">
                {selectedPlugin.options.length === 0 ? (
                  <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                    This plugin does not expose any user-configurable options.
                  </div>
                ) : (
                  <>
                    <div className="grid gap-4 xl:grid-cols-2">
                      {selectedPlugin.options.map((option) => (
                        <OptionEditor
                          key={option.key}
                          option={option}
                          value={draftValues[option.key]}
                          touched={touchedOptions.includes(option.key)}
                          disabled={pluginCatalogLoading}
                          onChange={(nextValue) => {
                            setDraftValues((current) => ({ ...current, [option.key]: nextValue }));
                            setTouchedOptions((current) => current.includes(option.key) ? current : [...current, option.key]);
                          }}
                        />
                      ))}
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <button
                        onClick={() => void savePluginOptions(selectedPlugin.pluginId, buildSavePayload(selectedPlugin, draftValues, touchedOptions))}
                        disabled={!hasDraftChanges || pluginCatalogLoading}
                        className="inline-flex items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground transition-colors hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        <Settings2 className="h-4 w-4" />
                        Save options
                      </button>
                      <button
                        onClick={() => {
                          setDraftValues(buildInitialDraftValues(selectedPlugin));
                          setTouchedOptions([]);
                        }}
                        disabled={!hasDraftChanges || pluginCatalogLoading}
                        className="rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        Discard changes
                      </button>
                    </div>
                  </>
                )}
              </Section>

              <Section title="Diagnostics">
                {selectedPlugin.validationIssues.length === 0 ? (
                  <div className="flex items-center gap-2 rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-700 dark:text-emerald-300">
                    <CheckCircle2 className="h-4 w-4" />
                    No plugin validation issues reported for this project.
                  </div>
                ) : (
                  <div className="space-y-3">
                    {selectedPlugin.validationIssues.map((issue) => (
                      <div
                        key={`${issue.path}:${issue.message}`}
                        className={cn(
                          'rounded-xl border p-4 text-sm',
                          issue.isWarning
                            ? 'border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300'
                            : 'border-destructive/30 bg-destructive/10 text-destructive'
                        )}
                      >
                        <div className="flex items-start gap-2">
                          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                          <div>
                            <div className="font-medium">{issue.path}</div>
                            <div className="mt-1">{issue.message}</div>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Section>

              <Section title="Installation">
                <div className="grid gap-3 md:grid-cols-2">
                  <MetadataRow label="Install path" value={selectedPlugin.installPath} />
                  <MetadataRow label="Git commit" value={selectedPlugin.gitCommitSha || 'n/a'} />
                  <MetadataRow label="Installed at" value={formatDateTime(selectedPlugin.installedAt)} />
                  <MetadataRow label="Last updated" value={formatDateTime(selectedPlugin.lastUpdated)} />
                </div>
              </Section>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

const Section = ({ title, children }: { title: string; children: ReactNode }) => (
  <section>
    <div className="mb-3 text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">{title}</div>
    {children}
  </section>
);

const StatusPill = ({ label, tone }: { label: string; tone: 'neutral' | 'success' | 'warning' }) => (
  <span
    className={cn(
      'inline-flex rounded-full px-2.5 py-1 text-xs font-medium',
      tone === 'success' && 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300',
      tone === 'warning' && 'bg-amber-500/15 text-amber-700 dark:text-amber-300',
      tone === 'neutral' && 'bg-accent text-muted-foreground'
    )}
  >
    {label}
  </span>
);

const MetricCard = ({ label, values }: { label: string; values: string[] }) => (
  <div className="rounded-xl border border-border p-4">
    <div className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">{label}</div>
    <div className="mt-2 text-2xl font-semibold text-foreground">{values.length}</div>
    <div className="mt-3 flex flex-wrap gap-2">
      {values.length > 0 ? values.map((value) => (
        <span key={value} className="rounded-md bg-accent px-2 py-1 text-xs text-foreground">
          {value}
        </span>
      )) : (
        <span className="text-sm text-muted-foreground">None</span>
      )}
    </div>
  </div>
);

const MetadataRow = ({ label, value }: { label: string; value: string }) => (
  <div className="rounded-xl border border-border p-4">
    <div className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">{label}</div>
    <div className="mt-2 break-all text-sm text-foreground">{value}</div>
  </div>
);

const OptionEditor = ({
  option,
  value,
  touched,
  disabled,
  onChange,
}: {
  option: PluginOption;
  value: string | boolean | undefined;
  touched: boolean;
  disabled: boolean;
  onChange: (value: string | boolean) => void;
}) => {
  const helperText = option.sensitive && option.hasValue && !touched
    ? 'Configured. Leave blank to keep the current secret.'
    : option.description;

  if (option.type === 'boolean' && !option.multiple) {
    return (
      <label className="rounded-xl border border-border p-4">
        <div className="flex items-center justify-between gap-4">
          <div>
            <div className="text-sm font-semibold text-foreground">{option.title}</div>
            <div className="mt-1 text-xs text-muted-foreground">{helperText}</div>
          </div>
          <input
            type="checkbox"
            checked={Boolean(value)}
            disabled={disabled}
            onChange={(event) => onChange(event.target.checked)}
            className="h-4 w-4 rounded border-border text-primary"
          />
        </div>
      </label>
    );
  }

  const textValue = typeof value === 'string' ? value : '';
  return (
    <label className="rounded-xl border border-border p-4">
      <div className="flex items-center gap-2">
        <div className="text-sm font-semibold text-foreground">{option.title}</div>
        {option.required && <span className="rounded-full bg-destructive/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-destructive">Required</span>}
        {option.sensitive && <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-700 dark:text-amber-300">Sensitive</span>}
      </div>
      <div className="mt-1 text-xs text-muted-foreground">{helperText}</div>
      {option.multiple ? (
        <textarea
          value={textValue}
          disabled={disabled}
          onChange={(event) => onChange(event.target.value)}
          rows={4}
          placeholder="One value per line"
          className="mt-3 w-full rounded-md border border-border bg-input px-3 py-2 text-sm text-foreground outline-none focus:border-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
        />
      ) : (
        <input
          value={textValue}
          type={option.type === 'number' ? 'number' : option.sensitive ? 'password' : 'text'}
          disabled={disabled}
          onChange={(event) => onChange(event.target.value)}
          placeholder={buildOptionPlaceholder(option)}
          min={option.type === 'number' ? option.min ?? undefined : undefined}
          max={option.type === 'number' ? option.max ?? undefined : undefined}
          className="mt-3 w-full rounded-md border border-border bg-input px-3 py-2 text-sm text-foreground outline-none focus:border-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
        />
      )}
    </label>
  );
};

function buildInitialDraftValues(plugin: Plugin): DraftValues {
  return Object.fromEntries(plugin.options.map((option) => [option.key, draftValueFromOption(option)]));
}

function draftValueFromOption(option: PluginOption): string | boolean {
  if (option.type === 'boolean' && !option.multiple) {
    return Boolean(option.value);
  }

  if (option.multiple) {
    return Array.isArray(option.value)
      ? option.value.map((entry) => String(entry)).join('\n')
      : '';
  }

  if (option.sensitive) {
    return '';
  }

  if (option.value === null || option.value === undefined) {
    return '';
  }

  return String(option.value);
}

function buildSavePayload(
  plugin: Plugin,
  draftValues: DraftValues,
  touchedOptions: string[],
): Record<string, unknown> {
  const touched = new Set(touchedOptions);
  const payload: Record<string, unknown> = {};

  for (const option of plugin.options) {
    if (!touched.has(option.key)) {
      continue;
    }

    const rawValue = draftValues[option.key];
    if (option.type === 'boolean' && !option.multiple) {
      payload[option.key] = Boolean(rawValue);
      continue;
    }

    const textValue = String(rawValue ?? '');
    if (option.multiple) {
      const values = textValue
        .split(/\r?\n/)
        .map((entry) => entry.trim())
        .filter(Boolean);
      payload[option.key] = values.length === 0
        ? null
        : option.type === 'number'
          ? values.map((entry) => Number(entry))
          : values;
      continue;
    }

    const trimmed = textValue.trim();
    if (!trimmed) {
      payload[option.key] = null;
      continue;
    }

    payload[option.key] = option.type === 'number' ? Number(trimmed) : trimmed;
  }

  return payload;
}

function buildOptionPlaceholder(option: PluginOption): string {
  if (option.sensitive && option.hasValue) {
    return 'Leave blank to keep current value';
  }

  if (option.defaultValue !== undefined && option.defaultValue !== null) {
    return `Default: ${String(option.defaultValue)}`;
  }

  return option.type === 'directory' || option.type === 'file'
    ? 'Enter a filesystem path'
    : 'Enter a value';
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return 'n/a';
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString();
}
