import { useAppStore } from '@/store';
import { mockDiffs } from '@/mocks/diffs';
import { cn } from '@/lib/utils';
import { FileCode, FilePlus, FileX, FileDiff, Copy, ExternalLink, Undo2 } from 'lucide-react';

export const ReviewPanel = () => {
  const { selectedThreadId, changedFiles, ui, selectChangedFile } = useAppStore();
  const files = changedFiles[selectedThreadId] || [];
  const selectedFile = ui.selectedChangedFile;
  const diff = selectedFile ? mockDiffs[selectedFile] : null;

  if (files.length === 0) {
    return (
      <div className="flex flex-col h-full border-l border-border surface-1">
        <div className="px-3 py-2 border-b border-border">
          <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Review</span>
        </div>
        <div className="flex-1 flex items-center justify-center text-center px-4">
          <div className="space-y-2">
            <FileDiff className="h-6 w-6 mx-auto text-muted-foreground/40" />
            <p className="text-xs text-muted-foreground">No changed files</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full border-l border-border surface-1 overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-border shrink-0">
        <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
          Review · {files.length} files
        </span>
      </div>

      {!diff ? (
        /* File list */
        <div className="flex-1 overflow-y-auto">
          {files.map(f => {
            const Icon = f.status === 'A' ? FilePlus : f.status === 'D' ? FileX : FileCode;
            return (
              <button
                key={f.path}
                onClick={() => selectChangedFile(f.path)}
                className={cn(
                  'flex w-full items-center gap-2 px-3 py-2 text-left hover:bg-accent/50 transition-colors border-b border-border/50',
                  selectedFile === f.path && 'bg-accent'
                )}
              >
                <span className={cn(
                  'flex h-5 w-5 items-center justify-center rounded text-[10px] font-bold shrink-0',
                  f.status === 'A' && 'bg-status-completed/15 text-status-completed',
                  f.status === 'M' && 'bg-primary/15 text-primary',
                  f.status === 'D' && 'bg-status-failed/15 text-status-failed',
                )}>
                  {f.status}
                </span>
                <span className="text-xs font-mono text-secondary-foreground truncate flex-1">{f.path}</span>
                <span className="text-[10px] font-mono text-muted-foreground shrink-0">
                  <span className="text-status-completed">+{f.additions}</span>{' '}
                  <span className="text-status-failed">-{f.deletions}</span>
                </span>
              </button>
            );
          })}
        </div>
      ) : (
        /* Diff view */
        <div className="flex-1 overflow-y-auto">
          <div className="flex items-center justify-between px-3 py-2 border-b border-border bg-muted/30">
            <button onClick={() => selectChangedFile(null)} className="text-xs text-primary hover:underline">
              ← All files
            </button>
            <div className="flex items-center gap-1">
              <button className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors" title="Copy path">
                <Copy className="h-3 w-3" />
              </button>
              <button className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors" title="Open in editor">
                <ExternalLink className="h-3 w-3" />
              </button>
              <button className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-accent transition-colors" title="Revert">
                <Undo2 className="h-3 w-3" />
              </button>
            </div>
          </div>
          <div className="px-0 font-mono text-xs">
            <div className="px-3 py-1.5 bg-muted/50 text-muted-foreground border-b border-border">
              {diff.filePath}
            </div>
            {diff.hunks.map((hunk, hi) => (
              <div key={hi}>
                <div className="px-3 py-1 text-primary/60 bg-[hsl(var(--diff-hunk))]">{hunk.header}</div>
                {hunk.lines.map((line, li) => (
                  <div
                    key={li}
                    className={cn(
                      'flex px-3 py-0 leading-5',
                      line.type === 'add' && 'bg-[hsl(var(--diff-add-bg))] text-[hsl(var(--diff-add-text))]',
                      line.type === 'del' && 'bg-[hsl(var(--diff-del-bg))] text-[hsl(var(--diff-del-text))]',
                      line.type === 'context' && 'text-muted-foreground',
                    )}
                  >
                    <span className="w-8 shrink-0 text-right pr-2 select-none opacity-50">
                      {line.oldLineNumber || ''}
                    </span>
                    <span className="w-8 shrink-0 text-right pr-2 select-none opacity-50">
                      {line.newLineNumber || ''}
                    </span>
                    <span className="w-4 shrink-0 select-none">
                      {line.type === 'add' ? '+' : line.type === 'del' ? '-' : ' '}
                    </span>
                    <span className="flex-1 whitespace-pre">{line.content}</span>
                  </div>
                ))}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
