import { useAppStore } from '@/store';
import { StatusBadge } from '@/components/StatusBadge';
import { CalendarClock, Play, Pause, ExternalLink } from 'lucide-react';

export const AutomationsPanel = () => {
  const { automations, projects, selectThread, setActiveView } = useAppStore();

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-border surface-2">
        <div className="flex items-center gap-2">
          <CalendarClock className="h-4 w-4 text-foreground" />
          <h2 className="text-sm font-semibold text-foreground">Automations</h2>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-3 space-y-2">
        {automations.map(auto => {
          const project = projects.find(p => p.id === auto.projectId);
          return (
            <div key={auto.id} className="rounded-lg border border-border bg-card p-3 space-y-2 hover:border-primary/20 transition-colors">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium text-foreground">{auto.title}</h3>
                <StatusBadge status={auto.status} />
              </div>
              <p className="text-xs text-muted-foreground">{auto.resultSummary}</p>
              <div className="flex items-center gap-3 text-[10px] text-muted-foreground">
                <span className="font-mono">{auto.cadence}</span>
                {project && <span>{project.name}</span>}
                <span>Last: {new Date(auto.lastRun).toLocaleDateString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</span>
                {auto.linkedThreadId && (
                  <button
                    onClick={() => { selectThread(auto.linkedThreadId!); setActiveView('threads'); }}
                    className="text-primary flex items-center gap-0.5 hover:underline"
                  >
                    <ExternalLink className="h-2.5 w-2.5" /> Thread
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
