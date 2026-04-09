type LogLevel = 'debug' | 'info' | 'warn' | 'error' | 'trace';

type TauriLogModule = {
  debug: (message: string) => Promise<void>;
  info: (message: string) => Promise<void>;
  warn: (message: string) => Promise<void>;
  error: (message: string) => Promise<void>;
  trace: (message: string) => Promise<void>;
};

let tauriLogModulePromise: Promise<TauriLogModule | null> | null = null;

function isTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

async function getTauriLogModule(): Promise<TauriLogModule | null> {
  if (!isTauriRuntime()) {
    return null;
  }

  if (!tauriLogModulePromise) {
    tauriLogModulePromise = import('@tauri-apps/plugin-log')
      .then((module) => ({
        debug: module.debug,
        info: module.info,
        warn: module.warn,
        error: module.error,
        trace: module.trace,
      }))
      .catch(() => null);
  }

  return tauriLogModulePromise;
}

function formatLogMessage(scope: string, details?: unknown): string {
  if (details === undefined) {
    return scope;
  }

  if (typeof details === 'string') {
    return `${scope} ${details}`;
  }

  try {
    return `${scope} ${JSON.stringify(details)}`;
  } catch {
    return `${scope} ${String(details)}`;
  }
}

export async function logToDesktop(level: LogLevel, scope: string, details?: unknown): Promise<void> {
  const tauriLog = await getTauriLogModule();
  if (!tauriLog) {
    return;
  }

  await tauriLog[level](formatLogMessage(scope, details));
}
