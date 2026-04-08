import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

const statusBadgeVariants = cva(
  'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
  {
    variants: {
      status: {
        idle: 'bg-muted text-muted-foreground',
        running: 'bg-status-running/15 text-status-running',
        waiting_approval: 'bg-status-waiting/15 text-status-waiting',
        completed: 'bg-status-completed/15 text-status-completed',
        failed: 'bg-status-failed/15 text-status-failed',
        scheduled: 'bg-status-scheduled/15 text-status-scheduled',
        active: 'bg-status-running/15 text-status-running',
        paused: 'bg-status-idle/15 text-muted-foreground',
        error: 'bg-status-failed/15 text-status-failed',
      },
    },
    defaultVariants: {
      status: 'idle',
    },
  }
);

interface StatusBadgeProps extends VariantProps<typeof statusBadgeVariants> {
  label?: string;
  className?: string;
  showDot?: boolean;
}

const statusLabels: Record<string, string> = {
  idle: 'Idle',
  running: 'Running',
  waiting_approval: 'Needs Review',
  completed: 'Completed',
  failed: 'Failed',
  scheduled: 'Scheduled',
  active: 'Active',
  paused: 'Paused',
  error: 'Error',
};

export const StatusBadge = ({ status, label, className, showDot = true }: StatusBadgeProps) => (
  <span className={cn(statusBadgeVariants({ status }), className)}>
    {showDot && (
      <span
        className={cn(
          'h-1.5 w-1.5 rounded-full',
          status === 'running' && 'bg-status-running animate-pulse-dot',
          status === 'idle' && 'bg-muted-foreground',
          status === 'waiting_approval' && 'bg-status-waiting',
          status === 'completed' && 'bg-status-completed',
          status === 'failed' && 'bg-status-failed',
          status === 'scheduled' && 'bg-status-scheduled',
          status === 'active' && 'bg-status-running animate-pulse-dot',
          status === 'paused' && 'bg-muted-foreground',
          status === 'error' && 'bg-status-failed',
        )}
      />
    )}
    {label || statusLabels[status || 'idle'] || status}
  </span>
);
