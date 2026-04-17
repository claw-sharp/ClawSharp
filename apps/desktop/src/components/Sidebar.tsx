import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import type { Thread } from '@/types';
import { StatusBadge } from '@/components/StatusBadge';
import {
  FolderGit2, Pin, Plus, Bot,
  Inbox, CalendarClock, PanelLeftClose, PanelLeftOpen, Plug,
} from 'lucide-react';

export const Sidebar = () => {
  const {
    projects, threads, selectedProjectId, selectedThreadId,
    selectProject, selectThread, createThread, openProjectPicker, ui, toggleLeftSidebar, setActiveView,
  } = useAppStore();

  const projectThreads = threads.filter(t => t.projectId === selectedProjectId);
  const pinnedThreads = projectThreads.filter(t => t.pinned);
  const otherThreads = projectThreads.filter(t => !t.pinned);

  if (ui.leftSidebarCollapsed) {
    return (
      <div className="flex flex-col items-center gap-2 border-r border-border surface-0 py-3 px-1 w-12">
        <button onClick={toggleLeftSidebar} className="rounded-md p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors">
          <PanelLeftOpen className="h-4 w-4" />
        </button>
        {projects.map(p => (
          <button
            key={p.id}
            onClick={() => void selectProject(p.id)}
            className={cn(
              'flex h-8 w-8 items-center justify-center rounded-md text-xs font-bold transition-colors',
              p.id === selectedProjectId
                ? 'bg-primary text-primary-foreground'
                : 'bg-accent text-muted-foreground hover:text-foreground'
            )}
            title={p.name}
          >
            {p.name[0]}
          </button>
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col border-r border-border surface-0 w-64 overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-border">
        <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Projects</span>
        <button onClick={toggleLeftSidebar} className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors">
          <PanelLeftClose className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Project list */}
      <div className="px-2 py-2 space-y-0.5">
        {projects.map(p => (
          <button
            key={p.id}
            onClick={() => void selectProject(p.id)}
            className={cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors',
              p.id === selectedProjectId
                ? 'bg-accent text-foreground'
                : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
            )}
          >
            <FolderGit2 className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate flex-1">{p.name}</span>
            {p.activeThreadCount > 0 && (
              <span className="text-[10px] font-mono text-muted-foreground">{p.activeThreadCount}</span>
            )}
          </button>
        ))}
      </div>

      {/* Divider + Nav */}
      <div className="px-2 py-1 space-y-0.5 border-t border-border mt-1 pt-2">
        <button
          onClick={() => setActiveView('inbox')}
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
            ui.activeView === 'inbox' ? 'bg-accent text-foreground' : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
          )}
        >
          <Inbox className="h-3.5 w-3.5" />
          <span>Inbox</span>
        </button>
        <button
          onClick={() => setActiveView('automations')}
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
            ui.activeView === 'automations' ? 'bg-accent text-foreground' : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
          )}
        >
          <CalendarClock className="h-3.5 w-3.5" />
          <span>Automations</span>
        </button>
        <button
          onClick={() => setActiveView('plugins')}
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
            ui.activeView === 'plugins' ? 'bg-accent text-foreground' : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
          )}
        >
          <Plug className="h-3.5 w-3.5" />
          <span>Plugins</span>
        </button>
        <button
          onClick={() => setActiveView('agents')}
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
            ui.activeView === 'agents' ? 'bg-accent text-foreground' : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
          )}
        >
          <Bot className="h-3.5 w-3.5" />
          <span>Agents</span>
        </button>
      </div>

      {/* Thread list */}
      <div className="flex items-center justify-between px-3 py-2 border-t border-border mt-1">
        <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Threads</span>
        <button
          onClick={() => void (selectedProjectId ? createThread() : openProjectPicker())}
          className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"
        >
          <Plus className="h-3.5 w-3.5" />
        </button>
      </div>

      <div className="flex-1 overflow-y-auto px-2 pb-2 space-y-0.5">
        {pinnedThreads.length > 0 && (
          <>
            {pinnedThreads.map(t => (
              <ThreadItem key={t.id} thread={t} selected={t.id === selectedThreadId} onSelect={selectThread} />
            ))}
            {otherThreads.length > 0 && <div className="h-px bg-border my-1.5" />}
          </>
        )}
        {otherThreads.map(t => (
          <ThreadItem key={t.id} thread={t} selected={t.id === selectedThreadId} onSelect={selectThread} />
        ))}
      </div>
    </div>
  );
};

const ThreadItem = ({ thread, selected, onSelect }: {
  thread: Thread;
  selected: boolean;
  onSelect: (id: string) => Promise<void>;
}) => {
  const store = useAppStore();
  return (
    <button
      onClick={() => {
        void onSelect(thread.id);
        store.setActiveView('threads');
      }}
      className={cn(
        'flex w-full flex-col gap-1 rounded-md px-2.5 py-2 text-left transition-colors',
        selected
          ? 'bg-accent text-foreground'
          : 'text-muted-foreground hover:text-foreground hover:bg-accent/50'
      )}
    >
      <div className="flex items-center gap-1.5">
        {thread.pinned && <Pin className="h-3 w-3 text-primary shrink-0" />}
        <Bot className="h-3 w-3 shrink-0" />
        <span className="truncate text-xs font-medium flex-1">{thread.title}</span>
      </div>
      <div className="flex items-center gap-2">
        <StatusBadge status={thread.status} className="text-[10px] px-1.5 py-0" />
        {thread.changedFilesCount > 0 && (
          <span className="text-[10px] font-mono text-muted-foreground">{thread.changedFilesCount} files</span>
        )}
      </div>
    </button>
  );
};
