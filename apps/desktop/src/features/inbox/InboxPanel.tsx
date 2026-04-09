import { useAppStore } from '@/store';
import { StatusBadge } from '@/components/StatusBadge';
import { cn } from '@/lib/utils';
import {
  Inbox, Mail, MailOpen, Archive, ExternalLink,
  AlertCircle, Bot, Info, CalendarClock,
} from 'lucide-react';

export const InboxPanel = () => {
  const { inboxItems, markInboxItemRead, selectThread, setActiveView, resolveApproval } = useAppStore();

  const typeIcon = (type: string) => {
    switch (type) {
      case 'review': return <Bot className="h-4 w-4 text-status-waiting" />;
      case 'automation': return <CalendarClock className="h-4 w-4 text-primary" />;
      case 'error': return <AlertCircle className="h-4 w-4 text-status-failed" />;
      default: return <Info className="h-4 w-4 text-muted-foreground" />;
    }
  };

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-border surface-2">
        <div className="flex items-center gap-2">
          <Inbox className="h-4 w-4 text-foreground" />
          <h2 className="text-sm font-semibold text-foreground">Inbox</h2>
          <span className="text-xs text-muted-foreground">
            {inboxItems.filter(i => !i.read).length} unread
          </span>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {inboxItems.map(item => (
          <div
            key={item.id}
            className={cn(
              'flex gap-3 px-4 py-3 border-b border-border/50 hover:bg-accent/30 transition-colors cursor-pointer',
              !item.read && 'bg-accent/10'
            )}
            onClick={() => {
              markInboxItemRead(item.id);
              if (item.threadId) {
                selectThread(item.threadId);
                setActiveView('threads');
              }
            }}
          >
            <div className="shrink-0 mt-0.5">{typeIcon(item.type)}</div>
            <div className="flex-1 min-w-0 space-y-1">
              <div className="flex items-center gap-2">
                {!item.read && <span className="h-1.5 w-1.5 rounded-full bg-primary shrink-0" />}
                <span className={cn('text-sm truncate', !item.read ? 'font-medium text-foreground' : 'text-secondary-foreground')}>
                  {item.title}
                </span>
              </div>
              <p className="text-xs text-muted-foreground line-clamp-2">{item.summary}</p>
              <div className="flex items-center gap-2">
                <span className="text-[10px] text-muted-foreground">
                  {new Date(item.timestamp).toLocaleDateString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}
                </span>
                {item.threadId && (
                  <span className="text-[10px] text-primary flex items-center gap-0.5">
                    <ExternalLink className="h-2.5 w-2.5" /> Thread
                  </span>
                )}
              </div>
              {item.approvalId && (
                <div className="flex items-center gap-2 pt-1">
                  <button
                    onClick={(event) => {
                      event.stopPropagation();
                      void resolveApproval(item.approvalId!, 'approved');
                    }}
                    className="rounded bg-status-completed/15 px-2 py-1 text-[10px] font-medium text-status-completed hover:bg-status-completed/20"
                  >
                    Approve
                  </button>
                  <button
                    onClick={(event) => {
                      event.stopPropagation();
                      void resolveApproval(item.approvalId!, 'rejected');
                    }}
                    className="rounded bg-status-failed/15 px-2 py-1 text-[10px] font-medium text-status-failed hover:bg-status-failed/20"
                  >
                    Reject
                  </button>
                </div>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
