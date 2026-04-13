import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Search, Settings, Bell, ChevronDown, Loader2, Code2, Folder, Rocket, TerminalSquare } from 'lucide-react';
import { useTheme } from 'next-themes';

const projectLaunchTargets = [
  { id: '__vscode__', label: 'VS Code', icon: Code2 },
  { id: '__antigravity__', label: 'Antigravity', icon: Rocket },
  { id: '__finder__', label: 'Finder', icon: Folder },
  { id: '__terminal__', label: 'Terminal', icon: TerminalSquare },
] as const;

export const TopBar = () => {
  const {
    selectedProjectId, projects, inboxItems, run, connection,
    openProjectPicker,
    toggleSettings, toggleCommandPalette, setActiveView, ui, openExternalEditor,
  } = useAppStore();
  const { resolvedTheme } = useTheme();
  const project = projects.find(p => p.id === selectedProjectId);
  const unreadCount = inboxItems.filter(i => !i.read).length;
  const isLightTheme = resolvedTheme === 'light';
  const markSrc = isLightTheme ? '/clawsharp-mark-light.png' : '/clawsharp-mark-dark.png';
  const wordmarkSrc = isLightTheme ? '/clawsharp-wordmark-light.png' : '/clawsharp-wordmark-dark.png';

  return (
    <div className="flex h-12 items-center justify-between border-b border-border surface-1 px-3 gap-2">
      {/* Left */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2">
          <img
            src={markSrc}
            alt=""
            className="h-5 w-5 shrink-0 object-contain"
          />
          <img
            src={wordmarkSrc}
            alt="ClawSharp"
            className="h-3.5 w-auto shrink-0 object-contain"
          />
        </div>
        {project ? (
          <>
            <span className="text-muted-foreground">/</span>
            <button
              onClick={() => void openProjectPicker()}
              className="flex items-center gap-1 text-sm text-secondary-foreground hover:text-foreground transition-colors"
            >
              {project.name}
              <ChevronDown className="h-3 w-3" />
            </button>
            {project.branch && (
              <span className="text-xs text-muted-foreground font-mono">{project.branch}</span>
            )}
            <div className="ml-2 flex items-center rounded-lg border border-border bg-muted/30 p-0.5">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8 gap-1.5 rounded-md px-2 text-xs"
                onClick={() => void openExternalEditor({ kind: 'project', projectId: project.id, editorCommand: '__vscode__' })}
              >
                <Code2 className="h-3.5 w-3.5" />
                <span>Open VS</span>
              </Button>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button type="button" variant="ghost" size="sm" className="h-8 w-8 rounded-md px-0" aria-label="Open project in another app">
                    <ChevronDown className="h-3.5 w-3.5" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start" className="w-48">
                  {projectLaunchTargets.map((target) => {
                    const Icon = target.icon;
                    return (
                      <DropdownMenuItem
                        key={target.id}
                        onClick={() => void openExternalEditor({ kind: 'project', projectId: project.id, editorCommand: target.id })}
                        className="gap-2"
                      >
                        <Icon className="h-4 w-4 text-muted-foreground" />
                        <span>{target.label}</span>
                      </DropdownMenuItem>
                    );
                  })}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </>
        ) : (
          <button
            onClick={() => void openProjectPicker()}
            className="rounded-md border border-border bg-muted/40 px-2 py-1 text-xs text-muted-foreground hover:text-foreground hover:border-primary/30 transition-colors"
          >
            Open Project
          </button>
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

        <div className="mr-2 flex items-center gap-1.5 text-xs text-muted-foreground font-mono">
          {connection.isBootstrapping && <Loader2 className="h-3 w-3 animate-spin" />}
          <span>{connection.statusLabel}</span>
        </div>

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
