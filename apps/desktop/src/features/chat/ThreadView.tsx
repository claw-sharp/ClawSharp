import { useState, useRef, useEffect } from 'react';
import { useAppStore } from '@/store';
import { StatusBadge } from '@/components/StatusBadge';
import { cn } from '@/lib/utils';
import {
  Send, Square, RotateCcw, Bot, User, FileCode,
  CheckCircle2, Circle, Loader2, ChevronDown,
} from 'lucide-react';
import type { Message, ToolProgressEvent } from '@/types';

export const ThreadView = () => {
  const {
    selectedThreadId, threads, messages, run,
    sendMockPrompt, cancelMockRun,
  } = useAppStore();

  const thread = threads.find(t => t.id === selectedThreadId);
  const threadMessages = messages[selectedThreadId] || [];
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [threadMessages.length, run.isStreaming]);

  if (!thread) {
    return (
      <div className="flex flex-1 items-center justify-center text-muted-foreground">
        <div className="text-center space-y-2">
          <Bot className="h-8 w-8 mx-auto opacity-40" />
          <p className="text-sm">Select a thread to get started</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      {/* Thread Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-2.5 surface-2">
        <div className="flex items-center gap-3 min-w-0">
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-foreground truncate">{thread.title}</h2>
            <p className="text-xs text-muted-foreground truncate">{thread.summary}</p>
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <StatusBadge status={thread.status} />
          <span className="text-[10px] font-mono text-muted-foreground px-1.5 py-0.5 rounded bg-muted">
            {thread.model}
          </span>
          <span className={cn(
            'text-[10px] font-mono px-1.5 py-0.5 rounded',
            thread.target === 'local' ? 'bg-muted text-muted-foreground' :
            thread.target === 'worktree' ? 'bg-primary/10 text-primary' :
            'bg-status-scheduled/10 text-status-scheduled'
          )}>
            {thread.target}
          </span>
        </div>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto px-4 py-4 space-y-4">
        {threadMessages.map(msg => (
          <MessageBubble key={msg.id} message={msg} />
        ))}
        {run.isRunning && run.isStreaming && (
          <div className="flex items-center gap-2 text-xs text-status-running py-2">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            <span>{run.progressLabel}</span>
          </div>
        )}
      </div>

      {/* Composer */}
      <PromptComposer
        threadId={selectedThreadId}
        isRunning={run.isRunning}
        onSend={sendMockPrompt}
        onCancel={cancelMockRun}
      />
    </div>
  );
};

const MessageBubble = ({ message }: { message: Message }) => {
  const isUser = message.role === 'user';

  return (
    <div className={cn('flex gap-3', isUser ? '' : '')}>
      <div className={cn(
        'flex h-6 w-6 shrink-0 items-center justify-center rounded-md mt-0.5',
        isUser ? 'bg-accent' : 'bg-primary/10'
      )}>
        {isUser ? <User className="h-3.5 w-3.5 text-foreground" /> : <Bot className="h-3.5 w-3.5 text-primary" />}
      </div>
      <div className="flex-1 min-w-0 space-y-2">
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium text-foreground">{isUser ? 'You' : 'ClawSharp'}</span>
          <span className="text-[10px] text-muted-foreground">
            {new Date(message.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </span>
          {message.isStreaming && (
            <span className="text-[10px] text-status-running flex items-center gap-1">
              <Loader2 className="h-2.5 w-2.5 animate-spin" /> Streaming
            </span>
          )}
        </div>
        <div className="text-sm text-secondary-foreground leading-relaxed whitespace-pre-wrap">
          {message.content}
          {message.isStreaming && <span className="inline-block w-1.5 h-4 bg-primary ml-0.5 animate-stream-cursor" />}
        </div>
        {message.toolProgress && message.toolProgress.length > 0 && (
          <ToolProgressList events={message.toolProgress} />
        )}
      </div>
    </div>
  );
};

const ToolProgressList = ({ events }: { events: ToolProgressEvent[] }) => {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <div className="mt-2 rounded-md border border-border bg-muted/30 overflow-hidden">
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="flex w-full items-center gap-2 px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
      >
        <FileCode className="h-3 w-3" />
        <span>{events.length} steps</span>
        <ChevronDown className={cn('h-3 w-3 ml-auto transition-transform', collapsed && '-rotate-90')} />
      </button>
      {!collapsed && (
        <div className="border-t border-border px-3 py-2 space-y-1">
          {events.map(e => (
            <div key={e.id} className="flex items-center gap-2 text-xs">
              {e.completed ? (
                <CheckCircle2 className="h-3 w-3 text-status-completed shrink-0" />
              ) : (
                <Circle className="h-3 w-3 text-muted-foreground shrink-0" />
              )}
              <span className={cn('flex-1', e.completed ? 'text-muted-foreground' : 'text-foreground')}>
                {e.label}
              </span>
              {e.detail && <span className="text-[10px] text-muted-foreground">{e.detail}</span>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

const PromptComposer = ({
  threadId,
  isRunning,
  onSend,
  onCancel,
}: {
  threadId: string;
  isRunning: boolean;
  onSend: (threadId: string, content: string) => void;
  onCancel: () => void;
}) => {
  const [value, setValue] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleSubmit = () => {
    if (!value.trim() || isRunning) return;
    onSend(threadId, value.trim());
    setValue('');
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  return (
    <div className="border-t border-border px-4 py-3 surface-2">
      <div className="flex items-end gap-2 rounded-lg border border-border bg-background p-2 focus-within:border-primary/40 transition-colors">
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Type a prompt..."
          rows={1}
          className="flex-1 resize-none bg-transparent text-sm text-foreground placeholder:text-muted-foreground outline-none min-h-[24px] max-h-[120px]"
          disabled={isRunning}
        />
        {isRunning ? (
          <button
            onClick={onCancel}
            className="flex items-center justify-center rounded-md bg-destructive p-1.5 text-destructive-foreground hover:bg-destructive/90 transition-colors"
          >
            <Square className="h-3.5 w-3.5" />
          </button>
        ) : (
          <button
            onClick={handleSubmit}
            disabled={!value.trim()}
            className="flex items-center justify-center rounded-md bg-primary p-1.5 text-primary-foreground hover:bg-primary/90 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            <Send className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
      <p className="text-[10px] text-muted-foreground mt-1.5">
        Press Enter to send · Shift+Enter for new line · {isRunning ? 'Esc to cancel' : '⌘K for commands'}
      </p>
    </div>
  );
};
