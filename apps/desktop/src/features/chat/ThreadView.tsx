import { useEffect, useRef, useState } from 'react';
import { useAppStore } from '@/store';
import { StatusBadge } from '@/components/StatusBadge';
import { HoverCard, HoverCardContent, HoverCardTrigger } from '@/components/ui/hover-card';
import { agentHostClient } from '@/lib/agentHostClient';
import { cn } from '@/lib/utils';
import {
  Send, Square, Bot, User, FileCode,
  CheckCircle2, Circle, Loader2, ChevronDown, RotateCcw, Archive, ShieldAlert, ExternalLink,
  TerminalSquare, Search, PencilLine, FlaskConical, Clock3, AlertTriangle, Gauge, Paperclip, ImagePlus, X,
} from 'lucide-react';
import type { Message, Skill, ToolProgressEvent } from '@/types';
import { buildPromptWithAttachments, parsePromptAttachments, type PromptAttachment } from '@/features/chat/promptAttachments';

export const ThreadView = () => {
  const {
    selectedProjectId, selectedThreadId, projects, threads, messages, threadHistory, inboxItems, run, connection, settings, skills, skillsLoading, workspaceFiles, workspaceFilesLoading,
    createThread, openProjectPicker, sendPrompt, cancelRun, retryThread, archiveThread, resolveApproval, setActiveView, toggleSettings, loadOlderThreadMessages, loadSkills, loadWorkspaceFiles,
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

  useEffect(() => {
    if (!selectedProjectId) {
      return;
    }

    void loadSkills(selectedProjectId);
    void loadWorkspaceFiles(selectedProjectId);
  }, [loadSkills, loadWorkspaceFiles, selectedProjectId]);

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
                    onClick={() => void resolveApproval(approval.approvalId!, 'always_allow')}
                    className="rounded bg-primary/10 px-2.5 py-1.5 text-xs font-medium text-primary hover:bg-primary/15"
                  >
                    Always Allow This Session
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
      </div>

      {/* Composer */}
      <PromptComposer
        threadId={thread.id}
        threadModel={thread.model}
        threadMessages={threadMessages}
        isRunning={run.isRunning}
        isBrowserPreview={isBrowserPreview}
        skills={skills}
        skillsLoading={skillsLoading}
        workspaceFiles={workspaceFiles}
        workspaceFilesLoading={workspaceFilesLoading}
        onSend={sendPrompt}
        onCancel={cancelRun}
      />
    </div>
  );
};

const MessageBubble = ({ message }: { message: Message }) => {
  const { selectedProjectId, projects, openExternalEditor, settings } = useAppStore();
  const isUser = message.role === 'user';
  const parsedPrompt = parsePromptAttachments(message.content);
  const hasText = parsedPrompt.prompt.trim().length > 0;
  const project = projects.find((item) => item.id === selectedProjectId);

  const openFileFromMessage = (match: FileReferenceMatch) => {
    if (!project) {
      return;
    }

    const absolutePath = isAbsoluteFilePath(match.path)
      ? match.path
      : `${project.path}/${match.path}`.replace(/\/+/g, '/');
    void openExternalEditor({
      kind: 'position',
      path: absolutePath,
      line: match.line,
      column: match.column,
      editorCommand: settings.editorPath,
    });
  };

  const openAttachment = (attachment: PromptAttachment) => {
    void openExternalEditor({
      kind: 'file',
      path: attachment.path,
      editorCommand: settings.editorPath,
    });
  };

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
        {hasText && (
          <div className="text-sm text-secondary-foreground leading-relaxed whitespace-pre-wrap">
            {renderMessageContent(parsedPrompt.prompt, openFileFromMessage)}
            {message.isStreaming && <span className="inline-block w-1.5 h-4 bg-primary ml-0.5 animate-stream-cursor" />}
          </div>
        )}
        {parsedPrompt.attachments.length > 0 && (
          <AttachmentPills attachments={parsedPrompt.attachments} onOpenAttachment={openAttachment} />
        )}
        {message.toolProgress && message.toolProgress.length > 0 && (
          <ToolProgressList events={message.toolProgress} />
        )}
      </div>
    </div>
  );
};

const AttachmentPills = ({
  attachments,
  onOpenAttachment,
}: {
  attachments: PromptAttachment[];
  onOpenAttachment: (attachment: PromptAttachment) => void;
}) => (
  <div className="flex flex-wrap gap-2">
    {attachments.map((attachment) => (
      <button
        key={`${attachment.kind}:${attachment.path}`}
        type="button"
        onClick={() => onOpenAttachment(attachment)}
        className="inline-flex items-center gap-1.5 rounded-full border border-border/80 bg-muted/40 px-2.5 py-1 text-[11px] text-secondary-foreground transition-colors hover:border-primary/30 hover:text-foreground"
      >
        {attachment.kind === 'image' ? <ImagePlus className="h-3 w-3" /> : <Paperclip className="h-3 w-3" />}
        <span className="font-medium">{attachment.name}</span>
      </button>
    ))}
  </div>
);


type FileReferenceMatch = {
  path: string;
  line: number | null;
  column: number | null;
  label: string;
  start: number;
  end: number;
  appearance: 'plain' | 'code' | 'markdown';
};

function renderMessageContent(content: string, onOpenFile: (match: FileReferenceMatch) => void) {
  const tokens = findMessageTokens(content);
  if (tokens.length === 0) {
    return content;
  }

  const nodes: Array<string | JSX.Element> = [];
  let cursor = 0;

  tokens.forEach((token, index) => {
    if (cursor < token.start) {
      nodes.push(content.slice(cursor, token.start));
    }

    if (token.kind === 'text') {
      nodes.push(content.slice(token.start, token.end));
    } else {
      nodes.push(
        <button
          key={`${token.match.path}-${token.start}-${index}`}
          type="button"
          onClick={() => onOpenFile(token.match)}
          className={cn(
            'cursor-pointer underline underline-offset-2 hover:text-primary/80',
            token.match.appearance === 'code'
              ? 'rounded-sm bg-muted px-1 font-mono text-primary'
              : 'font-mono text-primary',
          )}
        >
          {token.match.label}
        </button>,
      );
    }

    cursor = token.end;
  });

  if (cursor < content.length) {
    nodes.push(content.slice(cursor));
  }

  return nodes;
}

type MessageToken =
  | { kind: 'text'; start: number; end: number }
  | { kind: 'file'; start: number; end: number; match: FileReferenceMatch };

function findMessageTokens(content: string): MessageToken[] {
  const protectedTokens: Array<MessageToken & { priority: number }> = [];
  const markdownLinkPattern = /\[([^\]\n]+)\]\((<([^>\n]+)>|([^) \n]+))\)/g;
  const inlineCodePattern = /`([^`\n]+)`/g;

  for (const match of content.matchAll(markdownLinkPattern)) {
    const fullMatch = match[0];
    const label = match[1];
    const target = match[3] ?? match[4] ?? '';
    const parsedTarget = parseFileReferenceTarget(target);
    const start = match.index ?? 0;
    const end = start + fullMatch.length;

    protectedTokens.push(parsedTarget
      ? {
          kind: 'file',
          start,
          end,
          match: {
            ...parsedTarget,
            label,
            start,
            end,
            appearance: 'markdown',
          },
          priority: 0,
        }
      : { kind: 'text', start, end, priority: 0 });
  }

  for (const match of content.matchAll(inlineCodePattern)) {
    const fullMatch = match[0];
    const rawValue = match[1];
    const parsedTarget = parseFileReferenceTarget(rawValue);
    const start = match.index ?? 0;
    const end = start + fullMatch.length;

    protectedTokens.push(parsedTarget
      ? {
          kind: 'file',
          start,
          end,
          match: {
            ...parsedTarget,
            label: rawValue,
            start,
            end,
            appearance: 'code',
          },
          priority: 1,
        }
      : { kind: 'text', start, end, priority: 1 });
  }

  const plainMatches = findPlainFileReferenceMatches(content).map((match) => ({
    kind: 'file' as const,
    start: match.start,
    end: match.end,
    match,
    priority: 2,
  }));

  const allTokens = [...protectedTokens, ...plainMatches].sort((left, right) => {
    if (left.start !== right.start) {
      return left.start - right.start;
    }

    if (left.priority !== right.priority) {
      return left.priority - right.priority;
    }

    return left.end - right.end;
  });

  const tokens: MessageToken[] = [];
  let cursor = 0;

  for (const token of allTokens) {
    if (token.start < cursor) {
      continue;
    }

    tokens.push(token.kind === 'text'
      ? { kind: 'text', start: token.start, end: token.end }
      : { kind: 'file', start: token.start, end: token.end, match: token.match });
    cursor = token.end;
  }

  return tokens;
}

function findPlainFileReferenceMatches(content: string): FileReferenceMatch[] {
  const pattern = /(^|[\s(])((?:[A-Za-z0-9._-]+\/)+[A-Za-z0-9._-]+(?:\.[A-Za-z0-9._-]+)?)(?::(\d+))?(?::(\d+))?(?=$|[\s),.])/gm;
  const matches: FileReferenceMatch[] = [];

  for (const match of content.matchAll(pattern)) {
    const fullMatch = match[0];
    const leading = match[1] ?? '';
    const path = match[2];
    const line = match[3] ? Number(match[3]) : null;
    const column = match[4] ? Number(match[4]) : null;
    const start = (match.index ?? 0) + leading.length;
    const end = start + path.length + (match[3] ? `:${match[3]}`.length : 0) + (match[4] ? `:${match[4]}`.length : 0);

    if (!path.includes('/')) {
      continue;
    }

    if (!/[.]/.test(path.split('/').pop() ?? '')) {
      continue;
    }

    matches.push({ path, line, column, label: content.slice(start, end), start, end, appearance: 'plain' });
  }

  return matches;
}

function parseFileReferenceTarget(rawTarget: string) {
  const normalizedTarget = rawTarget.trim().replace(/^file:\/\//, '');
  const match = normalizedTarget.match(/^(.*?)(?::(\d+)(?::(\d+))?)?$/);
  if (!match) {
    return null;
  }

  const path = match[1];
  const line = match[2] ? Number(match[2]) : null;
  const column = match[3] ? Number(match[3]) : null;

  if (!isFileReferencePath(path)) {
    return null;
  }

  return { path, line, column };
}

function isFileReferencePath(path: string) {
  if ((!path.includes('/') && !path.includes('\\')) || !/[.]/.test(path.split(/[\\/]/).pop() ?? '')) {
    return false;
  }

  return true;
}

function isAbsoluteFilePath(path: string) {
  return path.startsWith('/') || /^[A-Za-z]:[\\/]/.test(path);
}

const ToolProgressList = ({ events }: { events: ToolProgressEvent[] }) => {
  const [collapsed, setCollapsed] = useState(true);
  const summary = summarizeToolEvents(events);

  return (
    <div className="mt-2 overflow-hidden rounded-xl border border-border/70 bg-muted/20">
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="flex w-full items-center gap-2 px-3 py-2 text-xs text-muted-foreground hover:text-foreground transition-colors"
      >
        <FileCode className="h-3.5 w-3.5" />
        <span className="font-medium text-foreground">{summary.title}</span>
        <span className="text-[10px] text-muted-foreground">{summary.subtitle}</span>
        <ChevronDown className={cn('h-3 w-3 ml-auto transition-transform', collapsed && '-rotate-90')} />
      </button>
      {!collapsed && (
        <div className="space-y-2 border-t border-border/70 px-3 py-3">
          {events.map(e => (
            <ToolProgressCard key={e.id} event={e} />
          ))}
        </div>
      )}
    </div>
  );
};

const ToolProgressCard = ({ event }: { event: ToolProgressEvent }) => {
  const Icon = iconForToolEvent(event);
  const stateLabel = event.status ?? (event.completed ? 'completed' : 'running');
  const toolName = event.toolName ?? event.label;

  return (
    <div className="rounded-lg border border-border/60 bg-background/60 px-3 py-2">
      <div className="flex items-start gap-2">
        <div className={cn(
          'mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-md',
          stateLabel === 'failed'
            ? 'bg-status-failed/15 text-status-failed'
            : stateLabel === 'completed'
              ? 'bg-status-completed/15 text-status-completed'
              : 'bg-primary/10 text-primary',
        )}>
          <Icon className="h-3.5 w-3.5" />
        </div>
        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex items-center gap-2">
            <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-[10px] text-foreground">
              {toolName}
            </span>
            <span className={cn(
              'text-[10px] uppercase tracking-wide',
              stateLabel === 'failed'
                ? 'text-status-failed'
                : stateLabel === 'completed'
                  ? 'text-status-completed'
                  : 'text-muted-foreground',
            )}>
              {stateLabel}
            </span>
          </div>
          <p className="text-xs text-foreground">{event.label}</p>
          {event.detail && (
            <pre className="overflow-x-auto rounded-md bg-black/20 px-2 py-2 font-mono text-[11px] leading-relaxed text-secondary-foreground whitespace-pre-wrap">
              {event.detail}
            </pre>
          )}
        </div>
        {stateLabel === 'failed' ? (
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-status-failed" />
        ) : event.completed ? (
          <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-status-completed" />
        ) : (
          <Circle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
        )}
      </div>
    </div>
  );
};

function summarizeToolEvents(events: ToolProgressEvent[]) {
  const running = events.filter((event) => (event.status ?? (event.completed ? 'completed' : 'running')) === 'running').length;
  const failed = events.filter((event) => (event.status ?? (event.completed ? 'completed' : 'running')) === 'failed').length;

  return {
    title: events.length === 1 ? 'Tool activity' : `${events.length} tool calls`,
    subtitle: failed > 0
      ? `${failed} failed`
      : running > 0
        ? `${running} running`
        : 'all completed',
  };
}

function iconForToolEvent(event: ToolProgressEvent) {
  const toolName = (event.toolName ?? '').toLowerCase();
  switch (event.type) {
    case 'searching':
      return Search;
    case 'editing':
      return PencilLine;
    case 'testing':
      return FlaskConical;
    case 'waiting':
      return Clock3;
    default:
      return toolName.includes('search') ? Search : TerminalSquare;
  }
}

const PromptComposer = ({
  threadId,
  threadModel,
  threadMessages,
  isRunning,
  isBrowserPreview,
  skills,
  skillsLoading,
  workspaceFiles,
  workspaceFilesLoading,
  onSend,
  onCancel,
}: {
  threadId: string;
  threadModel: string;
  threadMessages: Message[];
  isRunning: boolean;
  isBrowserPreview: boolean;
  skills: Skill[];
  skillsLoading: boolean;
  workspaceFiles: string[];
  workspaceFilesLoading: boolean;
  onSend: (threadId: string, prompt: string) => Promise<void>;
  onCancel: () => Promise<void>;
}) => {
  const { createThread, openProjectPicker, setActiveView, toggleSettings } = useAppStore();
  const [value, setValue] = useState('');
  const [draftAttachments, setDraftAttachments] = useState<PromptAttachment[]>([]);
  const [isPickingAttachment, setIsPickingAttachment] = useState(false);
  const [cursorPosition, setCursorPosition] = useState(0);
  const [selectedSuggestionIndex, setSelectedSuggestionIndex] = useState(0);
  const [dismissedSuggestionQuery, setDismissedSuggestionQuery] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const contextUsage = summarizeContextUsage(threadMessages, threadModel);
  const activeQuery = findActiveComposerQuery(value, cursorPosition);
  const suggestionQueryKey = activeQuery ? `${activeQuery.trigger}:${activeQuery.start}:${activeQuery.query}` : null;
  const slashCommands = createSlashCommands({
    createThread: () => void createThread(),
    openProjectPicker: () => void openProjectPicker(),
    openPlugins: () => setActiveView('plugins'),
    openInbox: () => setActiveView('inbox'),
    openSettings: () => toggleSettings(),
  });
  const suggestions = getComposerSuggestions(activeQuery, {
    skills,
    workspaceFiles,
    slashCommands,
  });
  const isSuggestionMenuOpen = !isRunning &&
    !isBrowserPreview &&
    activeQuery !== null &&
    dismissedSuggestionQuery !== suggestionQueryKey;
  const isSuggestionMenuLoading = activeQuery?.trigger === '$'
    ? skillsLoading
    : activeQuery?.trigger === '@'
      ? workspaceFilesLoading
      : false;
  const menuTitle = activeQuery?.trigger === '@'
    ? 'Files'
    : activeQuery?.trigger === '/'
      ? 'Commands'
      : 'Skills';

  useEffect(() => {
    setSelectedSuggestionIndex(0);
  }, [suggestionQueryKey]);

  const canSubmit = !isRunning && !isBrowserPreview && (value.trim().length > 0 || draftAttachments.length > 0);

  const submit = () => {
    if (!canSubmit) {
      return;
    }

    const prompt = buildPromptWithAttachments(value, draftAttachments);
    setValue('');
    setDraftAttachments([]);
    setCursorPosition(0);
    setDismissedSuggestionQuery(null);
    void onSend(threadId, prompt);
  };

  const pickAttachments = async (kind: PromptAttachment['kind']) => {
    if (isRunning || isBrowserPreview || isPickingAttachment) {
      return;
    }

    setIsPickingAttachment(true);
    try {
      const paths = kind === 'image'
        ? await agentHostClient.pickPromptImages()
        : await agentHostClient.pickPromptFiles();
      if (paths.length === 0) {
        return;
      }

      setDraftAttachments((current) => mergePromptAttachments(
        current,
        paths.map((path) => ({
          kind,
          path,
          name: getAttachmentName(path),
        })),
      ));
      requestAnimationFrame(() => textareaRef.current?.focus());
    } finally {
      setIsPickingAttachment(false);
    }
  };

  const removeDraftAttachment = (attachment: PromptAttachment) => {
    setDraftAttachments((current) => current.filter((item) => (
      item.kind !== attachment.kind || item.path !== attachment.path
    )));
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const applySelectedSuggestion = (suggestion: ComposerSuggestion) => {
    if (suggestion.action === 'execute') {
      setValue('');
      setCursorPosition(0);
      setDismissedSuggestionQuery(null);
      suggestion.execute();
      requestAnimationFrame(() => textareaRef.current?.focus());
      return;
    }

    if (!activeQuery) {
      return;
    }

    const nextValue = insertComposerSuggestion(value, activeQuery, suggestion.insertValue);
    setValue(nextValue);
    setDismissedSuggestionQuery(null);
    const nextCursorPosition = activeQuery.start + suggestion.trigger.length + suggestion.insertValue.length + 1;
    setCursorPosition(nextCursorPosition);

    requestAnimationFrame(() => {
      textareaRef.current?.focus();
      textareaRef.current?.setSelectionRange(nextCursorPosition, nextCursorPosition);
    });
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (isSuggestionMenuOpen) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedSuggestionIndex((index) => (
          suggestions.length === 0
            ? 0
            : (index + 1) % suggestions.length
        ));
        return;
      }

      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedSuggestionIndex((index) => (
          suggestions.length === 0
            ? 0
            : (index - 1 + suggestions.length) % suggestions.length
        ));
        return;
      }

      if ((e.key === 'Enter' || e.key === 'Tab') && suggestions.length > 0) {
        e.preventDefault();
        applySelectedSuggestion(suggestions[Math.min(selectedSuggestionIndex, suggestions.length - 1)]);
        return;
      }

      if (e.key === 'Escape') {
        e.preventDefault();
        setDismissedSuggestionQuery(suggestionQueryKey);
        return;
      }
    }

    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  return (
    <div className="border-t border-border px-4 py-3 surface-2">
      <div className="relative flex gap-3 rounded-lg border border-border bg-background p-2 focus-within:border-primary/40 transition-colors">
        {isSuggestionMenuOpen && (
          <div className="absolute inset-x-2 bottom-full mb-2 overflow-hidden rounded-lg border border-border bg-popover shadow-xl">
            <div className="border-b border-border/70 px-3 py-2 text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              {menuTitle}
            </div>
            <div role="listbox" aria-label={menuTitle} className="max-h-72 overflow-y-auto py-1">
              {isSuggestionMenuLoading ? (
                <div className="px-3 py-2 text-sm text-muted-foreground">
                  {activeQuery?.trigger === '@' ? 'Loading files…' : 'Loading skills…'}
                </div>
              ) : suggestions.length > 0 ? (
                suggestions.map((suggestion, index) => (
                  <button
                    key={suggestion.key}
                    type="button"
                    role="option"
                    aria-selected={index === selectedSuggestionIndex}
                    onMouseDown={(event) => {
                      event.preventDefault();
                      applySelectedSuggestion(suggestion);
                    }}
                    className={cn(
                      'flex w-full items-start justify-between gap-3 px-3 py-2 text-left transition-colors',
                      index === selectedSuggestionIndex
                        ? 'bg-primary/10 text-foreground'
                        : 'text-secondary-foreground hover:bg-muted/70 hover:text-foreground',
                    )}
                  >
                    <span className="min-w-0">
                      <span className="block font-mono text-sm text-foreground">{suggestion.label}</span>
                      <span className="block truncate text-xs text-muted-foreground">{suggestion.description}</span>
                    </span>
                    <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                      {suggestion.badge}
                    </span>
                  </button>
                ))
              ) : (
                <div className="px-3 py-2 text-sm text-muted-foreground">
                  {getEmptySuggestionText(activeQuery)}
                </div>
              )}
            </div>
          </div>
        )}
        <div className="min-w-0 flex-1 space-y-2">
          {draftAttachments.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {draftAttachments.map((attachment) => (
                <span
                  key={`${attachment.kind}:${attachment.path}`}
                  className="inline-flex items-center gap-1.5 rounded-full border border-border/80 bg-muted/40 px-2.5 py-1 text-[11px] text-secondary-foreground"
                >
                  {attachment.kind === 'image' ? <ImagePlus className="h-3 w-3" /> : <Paperclip className="h-3 w-3" />}
                  <span className="font-medium">{attachment.name}</span>
                  <button
                    type="button"
                    onClick={() => removeDraftAttachment(attachment)}
                    className="rounded-full p-0.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                    aria-label={`Remove ${attachment.name}`}
                  >
                    <X className="h-3 w-3" />
                  </button>
                </span>
              ))}
            </div>
          )}
          <textarea
            ref={textareaRef}
            value={value}
            onChange={(e) => {
              setValue(e.target.value);
              setCursorPosition(e.target.selectionStart ?? e.target.value.length);
              setDismissedSuggestionQuery(null);
            }}
            onKeyDown={handleKeyDown}
            onClick={(e) => {
              setCursorPosition(e.currentTarget.selectionStart ?? 0);
            }}
            onKeyUp={(e) => {
              setCursorPosition(e.currentTarget.selectionStart ?? 0);
            }}
            onSelect={(e) => {
              setCursorPosition(e.currentTarget.selectionStart ?? 0);
            }}
            placeholder={
              isBrowserPreview
                ? 'Browser preview is mock-only. Start the Tauri desktop app to chat for real.'
                : isRunning
                  ? 'ClawSharp is working…'
                  : 'Ask ClawSharp to work on this repository. Use / for commands, @ for files, and $ for skills.'
            }
            rows={3}
            className="w-full resize-none bg-transparent text-sm leading-relaxed text-foreground placeholder:text-muted-foreground outline-none min-h-[72px] max-h-[220px]"
            disabled={isRunning || isBrowserPreview}
          />
        </div>
        <div className="flex shrink-0 items-end gap-2">
          <div className="flex flex-col gap-1">
            <button
              type="button"
              disabled={isBrowserPreview || isRunning || isPickingAttachment}
              onClick={() => void pickAttachments('image')}
              aria-label="Attach photos"
              className={cn(
                'rounded-md border border-border px-2 py-1.5 text-xs text-muted-foreground transition-colors hover:border-primary/30 hover:text-foreground',
                (isBrowserPreview || isRunning || isPickingAttachment) && 'cursor-not-allowed opacity-50',
              )}
            >
              <ImagePlus className="h-3.5 w-3.5" />
            </button>
            <button
              type="button"
              disabled={isBrowserPreview || isRunning || isPickingAttachment}
              onClick={() => void pickAttachments('file')}
              aria-label="Attach files"
              className={cn(
                'rounded-md border border-border px-2 py-1.5 text-xs text-muted-foreground transition-colors hover:border-primary/30 hover:text-foreground',
                (isBrowserPreview || isRunning || isPickingAttachment) && 'cursor-not-allowed opacity-50',
              )}
            >
              <Paperclip className="h-3.5 w-3.5" />
            </button>
          </div>
          <button
            disabled={!isRunning && !canSubmit}
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
                : canSubmit
                  ? 'bg-primary hover:bg-primary/90'
                  : 'bg-primary/40 cursor-not-allowed',
            )}
          >
            {isRunning ? <Square className="h-3.5 w-3.5" /> : <Send className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>
      <div className="mt-1.5 flex items-center justify-between gap-3">
        <p className="text-[10px] text-muted-foreground">
          {isBrowserPreview
            ? 'This browser preview is read-only. Use `npm run dev` or `npm run dev:codex` for the real desktop runtime.'
            : `Shift+Enter for newline · Use / for actions, @ for workspace files, buttons for photos/files, and $ for skills · ${isRunning ? 'Cancel the current run to send another prompt.' : 'Streaming responses and tool progress are live.'}`}
        </p>
        {!isBrowserPreview && (
          <ContextUsageBadge
            usedPercent={contextUsage.usedPercent}
            usedTokens={contextUsage.usedTokens}
            effectiveContextWindow={contextUsage.effectiveContextWindow}
            remainingTokens={contextUsage.remainingTokens}
            autoCompactThreshold={contextUsage.autoCompactThreshold}
          />
        )}
      </div>
    </div>
  );
};

function mergePromptAttachments(
  current: PromptAttachment[],
  next: PromptAttachment[],
) {
  const merged = [...current];

  for (const attachment of next) {
    if (merged.some((item) => item.kind === attachment.kind && item.path === attachment.path)) {
      continue;
    }

    merged.push(attachment);
  }

  return merged;
}

function getAttachmentName(path: string) {
  const normalized = path.replace(/\\/g, '/');
  return normalized.split('/').pop() || normalized;
}

type ComposerQuery = {
  trigger: '$' | '@' | '/';
  query: string;
  start: number;
  end: number;
};

type SlashCommand = {
  id: string;
  label: string;
  description: string;
  execute: () => void;
};

type ComposerSuggestion = {
  key: string;
  trigger: '$' | '@' | '/';
  label: string;
  description: string;
  badge: string;
  action: 'insert' | 'execute';
  insertValue: string;
  execute: () => void;
};

function findActiveComposerQuery(value: string, selectionStart: number | null): ComposerQuery | null {
  if (selectionStart === null) {
    return null;
  }

  const beforeCursor = value.slice(0, selectionStart);
  const patterns: Array<{ trigger: ComposerQuery['trigger']; pattern: RegExp }> = [
    { trigger: '$', pattern: /(^|[\s(])\$(?<query>[A-Za-z0-9:_-]*)$/ },
    { trigger: '@', pattern: /(^|[\s(])@(?<query>[A-Za-z0-9_./-]*)$/ },
    { trigger: '/', pattern: /(^|[\s(])\/(?<query>[A-Za-z0-9-]*)$/ },
  ];

  const matches = patterns
    .map(({ trigger, pattern }) => {
      const match = pattern.exec(beforeCursor);
      if (!match) {
        return null;
      }

      const query = match.groups?.query ?? '';
      return {
        trigger,
        query,
        start: beforeCursor.length - query.length - 1,
        end: selectionStart,
      } satisfies ComposerQuery;
    })
    .filter((match): match is ComposerQuery => match !== null);

  if (matches.length === 0) {
    return null;
  }

  return matches.sort((left, right) => right.start - left.start)[0];
}

function createSlashCommands(actions: {
  createThread: () => void;
  openProjectPicker: () => void;
  openPlugins: () => void;
  openInbox: () => void;
  openSettings: () => void;
}): SlashCommand[] {
  return [
    {
      id: 'new-thread',
      label: '/new-thread',
      description: 'Create a new thread in the current project.',
      execute: actions.createThread,
    },
    {
      id: 'open-project',
      label: '/open-project',
      description: 'Open another local repository.',
      execute: actions.openProjectPicker,
    },
    {
      id: 'plugins',
      label: '/plugins',
      description: 'Open the Plugins panel.',
      execute: actions.openPlugins,
    },
    {
      id: 'inbox',
      label: '/inbox',
      description: 'Open the inbox and pending approvals.',
      execute: actions.openInbox,
    },
    {
      id: 'settings',
      label: '/settings',
      description: 'Open desktop runtime settings.',
      execute: actions.openSettings,
    },
  ];
}

function getComposerSuggestions(
  activeQuery: ComposerQuery | null,
  options: {
    skills: Skill[];
    workspaceFiles: string[];
    slashCommands: SlashCommand[];
  },
): ComposerSuggestion[] {
  if (!activeQuery) {
    return [];
  }

  const normalizedQuery = activeQuery.query.trim().toLowerCase();
  switch (activeQuery.trigger) {
    case '$':
      return [...options.skills]
        .filter((skill) => normalizedQuery.length === 0 || skill.name.toLowerCase().includes(normalizedQuery))
        .sort((left, right) => compareSuggestionLabels(left.name, right.name, normalizedQuery))
        .slice(0, 12)
        .map((skill) => ({
          key: `skill:${skill.name}`,
          trigger: '$',
          label: `$${skill.name}`,
          description: skill.source,
          badge: 'Enter',
          action: 'insert',
          insertValue: skill.name,
          execute: () => undefined,
        }));
    case '@':
      return [...options.workspaceFiles]
        .filter((filePath) => normalizedQuery.length === 0 || filePath.toLowerCase().includes(normalizedQuery))
        .sort((left, right) => compareSuggestionLabels(left, right, normalizedQuery))
        .slice(0, 14)
        .map((filePath) => ({
          key: `file:${filePath}`,
          trigger: '@',
          label: `@${filePath}`,
          description: filePath.split('/').slice(0, -1).join('/') || 'workspace root',
          badge: 'Attach',
          action: 'insert',
          insertValue: filePath,
          execute: () => undefined,
        }));
    case '/':
      return options.slashCommands
        .filter((command) => normalizedQuery.length === 0 || command.label.slice(1).toLowerCase().includes(normalizedQuery))
        .sort((left, right) => compareSuggestionLabels(left.label.slice(1), right.label.slice(1), normalizedQuery))
        .map((command) => ({
          key: `slash:${command.id}`,
          trigger: '/',
          label: command.label,
          description: command.description,
          badge: 'Run',
          action: 'execute',
          insertValue: command.label.slice(1),
          execute: command.execute,
        }));
    default:
      return [];
  }
}

function compareSuggestionLabels(left: string, right: string, normalizedQuery: string) {
  const leftLabel = left.toLowerCase();
  const rightLabel = right.toLowerCase();
  const leftStartsWith = normalizedQuery.length > 0 && leftLabel.startsWith(normalizedQuery);
  const rightStartsWith = normalizedQuery.length > 0 && rightLabel.startsWith(normalizedQuery);

  if (leftStartsWith !== rightStartsWith) {
    return leftStartsWith ? -1 : 1;
  }

  if (leftLabel.length !== rightLabel.length) {
    return leftLabel.length - rightLabel.length;
  }

  return leftLabel.localeCompare(rightLabel);
}

function insertComposerSuggestion(value: string, query: ComposerQuery, insertValue: string): string {
  return `${value.slice(0, query.start)}${query.trigger}${insertValue} ${value.slice(query.end)}`;
}

function getEmptySuggestionText(activeQuery: ComposerQuery | null): string {
  if (!activeQuery) {
    return 'No suggestions available.';
  }

  const normalizedQuery = activeQuery.query.trim();
  switch (activeQuery.trigger) {
    case '@':
      return normalizedQuery
        ? `No files match "@${normalizedQuery}".`
        : 'No files discovered for this workspace.';
    case '/':
      return normalizedQuery
        ? `No slash command matches "/${normalizedQuery}".`
        : 'No slash commands available.';
    case '$':
    default:
      return normalizedQuery
        ? `No skills match "$${normalizedQuery}".`
        : 'No skills discovered for this project.';
  }
}

const ContextUsageBadge = ({
  usedPercent,
  usedTokens,
  effectiveContextWindow,
  remainingTokens,
  autoCompactThreshold,
}: {
  usedPercent: number;
  usedTokens: number;
  effectiveContextWindow: number;
  remainingTokens: number;
  autoCompactThreshold: number;
}) => {
  return (
    <HoverCard openDelay={0}>
      <HoverCardTrigger asChild>
        <button
          type="button"
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-border/80 bg-muted/30 px-2.5 py-1 text-[10px] font-medium text-muted-foreground transition-colors hover:border-primary/30 hover:text-foreground"
        >
          <Gauge className="h-3 w-3" />
          <span>{`Context ~${usedPercent}%`}</span>
        </button>
      </HoverCardTrigger>
      <HoverCardContent align="end" className="w-72 space-y-2 px-3 py-3">
        <div className="space-y-1">
          <p className="text-xs font-semibold text-foreground">Context window</p>
          <p className="text-xs text-muted-foreground">
            {`${usedPercent}% full`}
          </p>
        </div>
        <div className="space-y-1 text-xs text-muted-foreground">
          <p>{`${formatTokenCount(usedTokens)} / ${formatTokenCount(effectiveContextWindow)} tokens used`}</p>
          <p>{`${formatTokenCount(remainingTokens)} tokens remaining before the effective window fills`}</p>
          <p>{`Auto-compaction starts near ${formatTokenCount(autoCompactThreshold)} tokens`}</p>
          <p>ClawSharp automatically compacts the active transcript before overflow when possible.</p>
        </div>
      </HoverCardContent>
    </HoverCard>
  );
};

const COMPACT_BOUNDARY_LABEL = 'Conversation compacted';
const DEFAULT_CONTEXT_WINDOW_TOKENS = 200_000;
const ONE_MILLION_CONTEXT_WINDOW_TOKENS = 1_000_000;
const AUTO_COMPACT_BUFFER_TOKENS = 13_000;
const COMPACT_SUMMARY_OUTPUT_RESERVE = 20_000;
const MAX_OUTPUT_TOKENS_DEFAULT = 32_000;
const MAX_OUTPUT_TOKENS_UPPER_LIMIT = 64_000;

function summarizeContextUsage(messages: Message[], model: string) {
  const activeMessages = getMessagesAfterCompactBoundary(messages);
  const usedTokens = estimateMessageTokens(activeMessages);
  const effectiveContextWindow = getEffectiveContextWindowSize(model);
  const usedPercent = Math.max(0, Math.min(100, Math.round((usedTokens / Math.max(effectiveContextWindow, 1)) * 100)));
  const remainingTokens = Math.max(effectiveContextWindow - usedTokens, 0);
  const autoCompactThreshold = Math.max(getAutoCompactThreshold(model), 0);

  return {
    usedTokens,
    effectiveContextWindow,
    usedPercent,
    remainingTokens,
    autoCompactThreshold,
  };
}

function getMessagesAfterCompactBoundary(messages: Message[]) {
  for (let index = messages.length - 1; index >= 0; index -= 1) {
    const message = messages[index];
    if (message.role === 'system' && message.content.trim() === COMPACT_BOUNDARY_LABEL) {
      return messages.slice(index + 1);
    }
  }

  return messages;
}

function estimateMessageTokens(messages: Message[]) {
  let estimatedTokens = 0;

  for (const message of messages) {
    estimatedTokens += 4;
    estimatedTokens += estimateStringTokens(message.role);
    estimatedTokens += 2;
    estimatedTokens += estimateStringTokens('text');
    estimatedTokens += estimateStringTokens(message.content);

    for (const event of message.toolProgress ?? []) {
      estimatedTokens += 2;
      estimatedTokens += estimateStringTokens(event.id);
      estimatedTokens += estimateStringTokens(event.type);
      estimatedTokens += estimateStringTokens(event.toolName);
      estimatedTokens += estimateStringTokens(event.label);
      estimatedTokens += estimateStringTokens(event.detail);
      estimatedTokens += estimateStringTokens(event.status);
    }
  }

  return Math.max(estimatedTokens, 1);
}

function estimateStringTokens(value: string | undefined) {
  if (!value || value.trim().length === 0) {
    return 0;
  }

  return Math.ceil(value.length / 4);
}

function getAutoCompactThreshold(model: string) {
  return getEffectiveContextWindowSize(model) - AUTO_COMPACT_BUFFER_TOKENS;
}

function getEffectiveContextWindowSize(model: string) {
  const reservedTokens = Math.min(getMaxOutputTokensForModel(model), COMPACT_SUMMARY_OUTPUT_RESERVE);
  return getContextWindowForModel(model) - reservedTokens;
}

function getContextWindowForModel(model: string) {
  const normalizedModel = model.trim().toLowerCase();
  if (
    normalizedModel.includes('[1m]')
    || normalizedModel.includes('sonnet-4-6')
    || normalizedModel.includes('opus-4-6')
  ) {
    return ONE_MILLION_CONTEXT_WINDOW_TOKENS;
  }

  return DEFAULT_CONTEXT_WINDOW_TOKENS;
}

function getMaxOutputTokensForModel(model: string) {
  const normalizedModel = model.trim().toLowerCase();

  if (normalizedModel.includes('opus-4-6')) {
    return 64_000;
  }

  if (normalizedModel.includes('sonnet-4-6')) {
    return 32_000;
  }

  if (
    normalizedModel.includes('opus-4-5')
    || normalizedModel.includes('sonnet-4')
    || normalizedModel.includes('haiku-4')
  ) {
    return 32_000;
  }

  if (normalizedModel.includes('opus-4-1') || normalizedModel.includes('opus-4-')) {
    return 32_000;
  }

  if (normalizedModel.includes('claude-3-opus')) {
    return 4_096;
  }

  if (normalizedModel.includes('claude-3-sonnet')) {
    return 8_192;
  }

  if (normalizedModel.includes('claude-3-haiku')) {
    return 4_096;
  }

  if (normalizedModel.includes('3-5-sonnet') || normalizedModel.includes('3-5-haiku')) {
    return 8_192;
  }

  if (normalizedModel.includes('3-7-sonnet')) {
    return 32_000;
  }

  if (normalizedModel.includes('gpt-5') || normalizedModel.includes('codex')) {
    return 32_000;
  }

  if (normalizedModel.includes('gpt-4.1') || normalizedModel.includes('gpt-4o')) {
    return 16_384;
  }

  if (normalizedModel.includes('gemini')) {
    return 8_192;
  }

  return MAX_OUTPUT_TOKENS_DEFAULT > MAX_OUTPUT_TOKENS_UPPER_LIMIT
    ? MAX_OUTPUT_TOKENS_UPPER_LIMIT
    : MAX_OUTPUT_TOKENS_DEFAULT;
}

function formatTokenCount(value: number) {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, '')}M`;
  }

  if (value >= 1_000) {
    return `${Math.round(value / 1_000)}k`;
  }

  return value.toString();
}
