import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import { Activity, AlertCircle, ChevronDown, ChevronUp, ScrollText, Terminal } from 'lucide-react';

const tabConfig = [
  { id: 'logs', label: 'Logs', icon: ScrollText },
  { id: 'terminal', label: 'Terminal', icon: Terminal },
  { id: 'diagnostics', label: 'Diagnostics', icon: Activity },
] as const;

export const BottomDrawer = () => {
  const {
    selectedThreadId,
    logs,
    terminalOutput,
    diagnostics,
    ui,
    run,
    connection,
    setBottomDrawerTab,
    toggleBottomDrawer,
  } = useAppStore();

  const activeThreadId = run.activeThreadId ?? selectedThreadId;
  const threadLogs = activeThreadId ? (logs[activeThreadId] ?? []) : [];
  const terminalLines = activeThreadId ? (terminalOutput[activeThreadId] ?? []) : [];
  const diagnostic = activeThreadId ? diagnostics[activeThreadId] : undefined;

  return (
    <div className="flex h-full flex-col border-t border-border surface-0">
      <div className="flex items-center justify-between border-b border-border px-3 py-2">
        <div className="flex items-center gap-1">
          {tabConfig.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              onClick={() => setBottomDrawerTab(id)}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs transition-colors',
                ui.bottomDrawerTab === id
                  ? 'bg-accent text-foreground'
                  : 'text-muted-foreground hover:bg-accent/50 hover:text-foreground',
              )}
            >
              <Icon className="h-3.5 w-3.5" />
              <span>{label}</span>
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          {connection.errorMessage && (
            <span className="inline-flex items-center gap-1 text-[11px] text-status-failed">
              <AlertCircle className="h-3.5 w-3.5" />
              <span className="max-w-64 truncate">{connection.errorMessage}</span>
            </span>
          )}
          <button
            onClick={toggleBottomDrawer}
            className="rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground transition-colors"
            title={ui.bottomDrawerOpen ? 'Collapse drawer' : 'Expand drawer'}
          >
            {ui.bottomDrawerOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronUp className="h-4 w-4" />}
          </button>
        </div>
      </div>
      <div className="flex-1 overflow-hidden">
        {ui.bottomDrawerTab === 'logs' && <LogsTab logs={threadLogs} />}
        {ui.bottomDrawerTab === 'terminal' && <TerminalTab lines={terminalLines} />}
        {ui.bottomDrawerTab === 'diagnostics' && <DiagnosticsTab diagnostic={diagnostic} statusLabel={connection.statusLabel} />}
      </div>
    </div>
  );
};

const LogsTab = ({ logs }: { logs: ReturnType<typeof useAppStore.getState>['logs'][string] }) => {
  if (!logs.length) {
    return <EmptyState message="No log events for this thread yet." />;
  }

  return (
    <div className="h-full overflow-y-auto px-3 py-2 font-mono text-xs">
      <div className="space-y-1.5">
        {logs.map((entry) => (
          <div key={entry.id} className="grid grid-cols-[72px_88px_1fr] gap-3 rounded-md border border-border/60 px-2 py-1.5">
            <span className="text-muted-foreground">{formatTime(entry.timestamp)}</span>
            <span className={cn('uppercase', levelClassName(entry.level))}>{entry.stage}</span>
            <span className="text-secondary-foreground">{entry.message}</span>
          </div>
        ))}
      </div>
    </div>
  );
};

const TerminalTab = ({ lines }: { lines: string[] }) => {
  if (!lines.length) {
    return <EmptyState message="No terminal output captured for this thread." />;
  }

  return (
    <pre className="h-full overflow-auto bg-black/20 px-3 py-2 font-mono text-xs text-secondary-foreground">
      {lines.join('\n')}
    </pre>
  );
};

const DiagnosticsTab = ({
  diagnostic,
  statusLabel,
}: {
  diagnostic?: ReturnType<typeof useAppStore.getState>['diagnostics'][string];
  statusLabel: string;
}) => {
  if (!diagnostic) {
    return <EmptyState message={`No diagnostics loaded. Current status: ${statusLabel}.`} />;
  }

  const rows = [
    ['Provider', diagnostic.provider],
    ['Model', diagnostic.model],
    ['Transport', diagnostic.transport],
    ['Base URL', diagnostic.baseUrl],
    ['Config', diagnostic.configPath],
    ['Environment', diagnostic.environment],
    ['Uptime', diagnostic.uptime],
    ['Memory', diagnostic.memoryUsage],
    ['Debug Log', diagnostic.debugLogPath],
  ].filter(([, value]) => Boolean(value));

  return (
    <div className="h-full overflow-y-auto px-3 py-2 text-xs">
      <div className="grid gap-2">
        {rows.map(([label, value]) => (
          <div key={label} className="grid grid-cols-[96px_1fr] gap-3 rounded-md border border-border/60 px-2 py-1.5">
            <span className="text-muted-foreground">{label}</span>
            <span className="break-all text-secondary-foreground">{value}</span>
          </div>
        ))}
        {diagnostic.warnings.length > 0 && (
          <div className="rounded-md border border-status-warning/30 bg-status-warning/10 px-2 py-1.5">
            <div className="mb-1 font-medium text-status-warning">Warnings</div>
            <div className="space-y-1 text-secondary-foreground">
              {diagnostic.warnings.map((warning) => <div key={warning}>{warning}</div>)}
            </div>
          </div>
        )}
        {diagnostic.errors.length > 0 && (
          <div className="rounded-md border border-status-failed/30 bg-status-failed/10 px-2 py-1.5">
            <div className="mb-1 font-medium text-status-failed">Errors</div>
            <div className="space-y-1 text-secondary-foreground">
              {diagnostic.errors.map((error) => <div key={error}>{error}</div>)}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

const EmptyState = ({ message }: { message: string }) => (
  <div className="flex h-full items-center justify-center px-4 text-center text-xs text-muted-foreground">
    {message}
  </div>
);

function formatTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function levelClassName(level: 'info' | 'warn' | 'error' | 'debug'): string {
  switch (level) {
    case 'warn':
      return 'text-status-warning';
    case 'error':
      return 'text-status-failed';
    case 'debug':
      return 'text-primary/80';
    default:
      return 'text-status-completed';
  }
}
