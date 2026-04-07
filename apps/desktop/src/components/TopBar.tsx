import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import { Search, Settings, Bell, Command, ChevronDown, Zap } from 'lucide-react';

export const TopBar = () => {
  const {
    selectedProjectId, projects, inboxItems, run,
    toggleSettings, toggleCommandPalette, setActiveView, ui
  } = useAppStore();
  const project = projects.find(p => p.id === selectedProjectId);
  const unreadCount = inboxItems.filter(i => !i.read).length;

  return (
    <div className="flex h-12 items-center justify-between border-b border-border surface-1 px-3 gap-2">
      {/* Left */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2">
          <Zap className="h-4 w-4 text-primary" />
          <span className="text-sm font-semibold text-foreground">ClawSharp</span>
        </div>
        {project && (
          <>
            <span className="text-muted-foreground">/</span>
            <button className="flex items-center gap-1 text-sm text-secondary-foreground hover:text-foreground transition-colors">
              {project.name}
              <ChevronDown className="h-3 w-3" />
            </button>
            <span className="text-xs text-muted-foreground font-mono">{project.branch}</span>
          </>
        )}
      </div>

      {/* Center */}
      <button
        onClick={toggleCommandPalette}
        className="flex items-center gap-2 rounded-md border border-border bg-muted/50 px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground hover:border-primary/30 transition-colors min-w-[200px]"
      >
        <Search className="h-3 w-3" />
        <span>Search or run command...</span>
        <kbd className="ml-auto rounded border border-border bg-background px-1 py-0.5 text-[10px] font-mono">⌘K</kbd>
      </button>

      {/* Right */}
      <div className="flex items-center gap-1">
        {run.isRunning && (
          <div className="flex items-center gap-1.5 mr-2 text-xs text-status-running">
            <span className="h-1.5 w-1.5 rounded-full bg-status-running animate-pulse-dot" />
            <span className="font-mono">{run.progressLabel || 'Running...'}</span>
          </div>
        )}

        <span className="text-xs text-muted-foreground font-mono mr-2">anthropic / claude-4-sonnet</span>

        <button
          onClick={() => setActiveView('inbox')}
          className={cn(
            'relative rounded-md p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors',
            ui.activeView === 'inbox' && 'text-foreground bg-accent'
          )}
        >
          <Bell className="h-4 w-4" />
          {unreadCount > 0 && (
            <span className="absolute -right-0.5 -top-0.5 flex h-3.5 w-3.5 items-center justify-center rounded-full bg-primary text-[9px] font-bold text-primary-foreground">
              {unreadCount}
            </span>
          )}
        </button>

        <button
          onClick={toggleSettings}
          className="rounded-md p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"
        >
          <Settings className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
};
