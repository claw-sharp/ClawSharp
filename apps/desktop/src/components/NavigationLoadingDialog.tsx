import { Loader2 } from 'lucide-react';
import { useAppStore } from '@/store';

export const NavigationLoadingDialog = () => {
  const {
    ui: { navigationLoading },
  } = useAppStore();

  if (!navigationLoading) {
    return null;
  }

  return (
    <div
      aria-labelledby="navigation-loading-title"
      aria-describedby="navigation-loading-description"
      aria-modal="true"
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 px-4 backdrop-blur-sm"
      role="dialog"
    >
      <div className="w-full max-w-sm rounded-xl border border-border bg-card p-6 shadow-2xl">
        <div className="flex items-start gap-4">
          <div className="mt-0.5 rounded-full bg-primary/10 p-2 text-primary">
            <Loader2 className="h-5 w-5 animate-spin" />
          </div>
          <div className="space-y-1">
            <p
              className="text-[11px] font-semibold uppercase tracking-[0.2em] text-muted-foreground"
            >
              Please wait
            </p>
            <h2
              className="text-base font-semibold text-foreground"
              id="navigation-loading-title"
            >
              {navigationLoading.title}
            </h2>
            <p
              className="text-sm leading-relaxed text-muted-foreground"
              id="navigation-loading-description"
            >
              {navigationLoading.description}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};
