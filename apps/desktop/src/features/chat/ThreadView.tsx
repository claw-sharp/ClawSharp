import { useState, useRef, useEffect } from 'react';
import { useAppStore } from '@/store';
import { StatusBadge } from '@/components/StatusBadge';
import { cn } from '@/lib/utils';
import {
  Send, Square, Bot, User, FileCode,
  CheckCircle2, Circle, Loader2, ChevronDown, RotateCcw, Archive, ShieldAlert, ExternalLink,
} from 'lucide-react';
import type { Message, ToolProgressEvent } from '@/types';

export const ThreadView = () => {
  const {
    selectedProjectId, selectedThreadId, projects, threads, messages, threadHistory, inboxItems, run, connection, settings,
    createThread, openProjectPicker, sendPrompt, cancelRun, retryThread, archiveThread, resolveApproval, setActiveView, toggleSettings, loadOlderThreadMessages,
  } = useAppStore();

  const project = projects.find((item) => item.id === selectedProjectId);
  const thread = threads.find(t => t.id === selectedThreadId);
  const threadMessages = messages[selectedThreadId] || [];
  const history = selectedThreadId ? threadHistory[selectedThreadId] : undefined;
  const pendingApprovals = thread
    ? inboxItems.filter((item) => item.approvalId && item.threadId === thread.id)
    : [];
  const isBrowserPreview = settings.settingsIssues.some((issue) =>
    issue.includes('Browser preview uses mock AgentHost data.'));
  const scrollRef = useRef<HTMLDivElement>(null);
  const shouldPromptForProviderKeys = !settings.hasAnyConfiguredProviderCredential;

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [threadMessages.length, run.isStreaming]);

  if (connection.isBootstrapping) {
    return (
      <div className="flex flex-1 items-center justify-center text-muted-foreground">
        <div className="text-center space-y-3 max-w-sm px-6">
          <Loader2 className="h-8 w-8 mx-auto animate-spin text-primary" />
          <p className="text-sm text-foreground">Starting AgentHost...</p>
          <p className="text-xs text-muted-foreground">
            {connection.statusLabel || 'Loading desktop runtime state.'}
          </p>
        </div>
      </div>
    );
  }

  if (!project) {
    return (
      <div className="flex flex-1 items-center justify-center text-muted-foreground">
        <div className="text-center space-y-3 max-w-sm px-6">
          <Bot className="h-8 w-8 mx-auto opacity-40" />
          <p className="text-sm text-foreground">Open a local repository to start using the desktop runtime.</p>
          <p className="text-xs text-muted-foreground">{connection.errorMessage ?? 'AgentHost is connected, but no project is selected yet.'}</p>
          {shouldPromptForProviderKeys && (
            <div className="space-y-2">
              <p className="text-xs text-muted-foreground">
                No provider credentials are configured yet. Add a provider key before starting a thread.
              </p>
              <button
                onClick={toggleSettings}
                className="rounded-md border border-status-waiting/40 bg-status-waiting/15 px-3 py-2 text-xs font-medium text-status-waiting hover:bg-status-waiting/20 transition-colors"
              >
                Configure Provider Keys
              </button>
            </div>
          )}
          <button
            onClick={() => void openProjectPicker()}
            disabled={shouldPromptForProviderKeys}
            className={cn(
              'rounded-md px-3 py-2 text-xs font-medium transition-colors',
              shouldPromptForProviderKeys
                ? 'cursor-not-allowed bg-muted text-muted-foreground opacity-60'
                : 'bg-primary text-primary-foreground hover:bg-primary/90',
            )}
          >
            Open Project
          </button>
        </div>
      </div>
    );
  }

  if (!thread) {
    return (
      <div className="flex flex-1 items-center justify-center text-muted-foreground">
        <div className="text-center space-y-3 max-w-sm px-6">
          <Bot className="h-8 w-8 mx-auto opacity-40" />
          <p className="text-sm text-foreground">No thread selected for {project.name}.</p>
          <p className="text-xs text-muted-foreground">Create a thread to load persisted transcript history for this project.</p>
          {shouldPromptForProviderKeys && (
            <div className="space-y-2">
              <p className="text-xs text-muted-foreground">
                No provider credentials are configured yet. Add a provider key before starting a thread.
              </p>
              <button
                onClick={toggleSettings}
                className="rounded-md border border-status-waiting/40 bg-status-waiting/15 px-3 py-2 text-xs font-medium text-status-waiting hover:bg-status-waiting/20 transition-colors"
              >
                Configure Provider Keys
              </button>
            </div>
          )}
          <button
            onClick={() => void createThread()}
            className="rounded-md bg-primary px-3 py-2 text-xs font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
          >
            New Thread
          </button>
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
          {!run.isRunning && threadMessages.some((message) => message.role === 'user') && (
            <button
              onClick={() => void retryThread(thread.id)}
              className="rounded-md border border-border px-2 py-1 text-[10px] font-medium text-muted-foreground hover:text-foreground hover:border-primary/30 transition-colors"
            >
              <span className="inline-flex items-center gap-1">
                <RotateCcw className="h-3 w-3" /> Retry
              </span>
            </button>
          )}
          {!run.isRunning && (
            <button
              onClick={() => void archiveThread(thread.id)}
              className="rounded-md border border-border px-2 py-1 text-[10px] font-medium text-muted-foreground hover:text-foreground hover:border-primary/30 transition-colors"
            >
              <span className="inline-flex items-center gap-1">
                <Archive className="h-3 w-3" /> Archive
              </span>
            </button>
          )}
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
        {history?.hasMoreMessages && (
          <div className="flex justify-center">
            <button
              onClick={() => void loadOlderThreadMessages(thread.id)}
              disabled={history.isLoadingOlder}
              className={cn(
                'rounded-md border border-border px-3 py-2 text-xs font-medium transition-colors',
                history.isLoadingOlder
                  ? 'cursor-wait text-muted-foreground'
                  : 'text-muted-foreground hover:border-primary/30 hover:text-foreground',
              )}
            >
              <span className="inline-flex items-center gap-2">
                {history.isLoadingOlder && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                {history.isLoadingOlder ? 'Loading older messages…' : 'Load older messages'}
              </span>
            </button>
          </div>
        )}
        {shouldPromptForProviderKeys && (
          <div className="rounded-lg border border-status-waiting/30 bg-status-waiting/10 px-3 py-3 text-sm text-foreground">
            <div className="flex items-start justify-between gap-3">
              <div className="space-y-1">
                <p className="text-xs font-semibold uppercase tracking-wider text-status-waiting">Provider setup required</p>
                <p className="text-xs text-secondary-foreground">
                  No provider credentials are configured yet. Add a provider key before starting a new run.
                </p>
              </div>
              <button
                onClick={toggleSettings}
                className="rounded-md border border-status-waiting/40 bg-status-waiting/15 px-3 py-2 text-xs font-medium text-status-waiting hover:bg-status-waiting/20"
              >
                Configure Provider Keys
              </button>
            </div>
          </div>
        )}
        {pendingApprovals.map((approval) => (
          <div
            key={approval.id}
            className="rounded-lg border border-status-waiting/30 bg-status-waiting/10 px-3 py-3 text-sm text-foreground"
          >
            <div className="flex items-start gap-3">
              <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-status-waiting/15 text-status-waiting">
                <ShieldAlert className="h-4 w-4" />
              </div>
              <div className="min-w-0 flex-1 space-y-2">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wider text-status-waiting">Approval required</p>
                  <p className="mt-1 whitespace-pre-wrap text-sm text-foreground">{approval.summary}</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    onClick={() => void resolveApproval(approval.approvalId!, 'approved')}
                    className="rounded bg-status-completed/15 px-2.5 py-1.5 text-xs font-medium text-status-completed hover:bg-status-completed/20"
                  >
                    Approve
                  </button>
                  <button
                    onClick={() => void resolveApproval(approval.approvalId!, 'rejected')}
                    className="rounded bg-status-failed/15 px-2.5 py-1.5 text-xs font-medium text-status-failed hover:bg-status-failed/20"
                  >
                    Reject
                  </button>
                  <button
                    onClick={() => setActiveView('inbox')}
                    className="inline-flex items-center gap-1 rounded border border-border px-2.5 py-1.5 text-xs font-medium text-muted-foreground hover:border-primary/30 hover:text-foreground"
                  >
                    <ExternalLink className="h-3 w-3" />
                    Inbox
                  </button>
                </div>
              </div>
            </div>
          </div>
        ))}
        {isBrowserPreview && (
          <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-100">
            Browser preview is using mock AgentHost data. Run <code>npm run dev</code> for the real desktop shell, or <code>npm run dev:codex</code> to force the Codex dev path.
          </div>
        )}
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
        threadId={thread.id}
        isRunning={run.isRunning}
        isBrowserPreview={isBrowserPreview}
        onSend={sendPrompt}
        onCancel={cancelRun}
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
  isBrowserPreview,
  onSend,
  onCancel,
}: {
  threadId: string;
  isRunning: boolean;
  isBrowserPreview: boolean;
  onSend: (threadId: string, prompt: string) => Promise<void>;
  onCancel: () => Promise<void>;
}) => {
  const [value, setValue] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const submit = () => {
    const prompt = value.trim();
    if (!prompt || isRunning || isBrowserPreview) {
      return;
    }

    setValue('');
    void onSend(threadId, prompt);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit();
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
          placeholder={
            isBrowserPreview
              ? 'Browser preview is mock-only. Start the Tauri desktop app to chat for real.'
              : isRunning
                ? 'ClawSharp is working…'
                : 'Ask ClawSharp to work on this repository.'
          }
          rows={1}
          className="flex-1 resize-none bg-transparent text-sm text-foreground placeholder:text-muted-foreground outline-none min-h-[24px] max-h-[120px]"
          disabled={isRunning || isBrowserPreview}
        />
        <button
          disabled={isBrowserPreview || (!isRunning && value.trim().length === 0)}
          onClick={() => {
            if (isRunning) {
              void onCancel();
              return;
            }

            submit();
          }}
          className={cn(
            'flex items-center justify-center rounded-md p-1.5 text-primary-foreground transition-colors',
            isRunning
              ? 'bg-status-running hover:bg-status-running/80'
              : value.trim().length > 0
                ? 'bg-primary hover:bg-primary/90'
                : 'bg-primary/40 cursor-not-allowed',
          )}
        >
          {isRunning ? <Square className="h-3.5 w-3.5" /> : <Send className="h-3.5 w-3.5" />}
        </button>
      </div>
      <p className="text-[10px] text-muted-foreground mt-1.5">
        {isBrowserPreview
          ? 'This browser preview is read-only. Use `npm run dev` or `npm run dev:codex` for the real desktop runtime.'
          : `Shift+Enter for newline · ${isRunning ? 'Cancel the current run to send another prompt.' : 'Streaming responses and tool progress are live.'}`}
      </p>
    </div>
  );
};
