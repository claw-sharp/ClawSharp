import { useState, useEffect, useRef } from 'react';
import { useAppStore } from '@/store';
import { cn } from '@/lib/utils';
import { Search, FolderGit2, Bot, Settings, Inbox, CalendarClock } from 'lucide-react';

export const CommandPalette = () => {
  const { ui, toggleCommandPalette, projects, threads, selectProject, selectThread, setActiveView, toggleSettings } = useAppStore();
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (ui.commandPaletteOpen) {
      setQuery('');
      setSelectedIndex(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [ui.commandPaletteOpen]);

  if (!ui.commandPaletteOpen) return null;

  const items = [
    ...projects.map(p => ({ id: p.id, label: p.name, type: 'project' as const, icon: FolderGit2, action: () => { selectProject(p.id); setActiveView('threads'); } })),
    ...threads.map(t => ({ id: t.id, label: t.title, type: 'thread' as const, icon: Bot, action: () => { selectThread(t.id); setActiveView('threads'); } })),
    { id: 'inbox', label: 'Open Inbox', type: 'action' as const, icon: Inbox, action: () => setActiveView('inbox') },
    { id: 'automations', label: 'Open Automations', type: 'action' as const, icon: CalendarClock, action: () => setActiveView('automations') },
    { id: 'settings', label: 'Open Settings', type: 'action' as const, icon: Settings, action: () => toggleSettings() },
  ];

  const filtered = query
    ? items.filter(i => i.label.toLowerCase().includes(query.toLowerCase()))
    : items;

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setSelectedIndex(i => Math.min(i + 1, filtered.length - 1)); }
    if (e.key === 'ArrowUp') { e.preventDefault(); setSelectedIndex(i => Math.max(i - 1, 0)); }
    if (e.key === 'Enter' && filtered[selectedIndex]) {
      filtered[selectedIndex].action();
      toggleCommandPalette();
    }
    if (e.key === 'Escape') toggleCommandPalette();
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-[20vh]">
      <div className="absolute inset-0 bg-background/80 backdrop-blur-sm" onClick={toggleCommandPalette} />
      <div className="relative w-full max-w-md rounded-lg border border-border bg-card shadow-2xl overflow-hidden">
        <div className="flex items-center gap-2 px-3 py-2.5 border-b border-border">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input
            ref={inputRef}
            value={query}
            onChange={e => { setQuery(e.target.value); setSelectedIndex(0); }}
            onKeyDown={handleKeyDown}
            placeholder="Search projects, threads, actions..."
            className="flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground outline-none"
          />
        </div>
        <div className="max-h-64 overflow-y-auto py-1">
          {filtered.length === 0 && (
            <div className="px-3 py-4 text-center text-xs text-muted-foreground">No results</div>
          )}
          {filtered.map((item, idx) => (
            <button
              key={item.id}
              onClick={() => { item.action(); toggleCommandPalette(); }}
              className={cn(
                'flex w-full items-center gap-2 px-3 py-2 text-sm text-left transition-colors',
                idx === selectedIndex ? 'bg-accent text-foreground' : 'text-secondary-foreground hover:bg-accent/50'
              )}
            >
              <item.icon className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
              <span className="flex-1 truncate">{item.label}</span>
              <span className="text-[10px] text-muted-foreground capitalize">{item.type}</span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
};
