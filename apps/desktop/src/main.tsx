import { createRoot } from "react-dom/client";
import App from "./App.tsx";
import "./index.css";

async function attachTauriLogging(): Promise<void> {
  if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window)) {
    return;
  }

  try {
    const { attachLogger, trace, debug, info, warn, error } = await import("@tauri-apps/plugin-log");
    const originalConsole = {
      log: console.log.bind(console),
      debug: console.debug.bind(console),
      info: console.info.bind(console),
      warn: console.warn.bind(console),
      error: console.error.bind(console),
    };

    await attachLogger(({ level, message }) => {
      switch (level) {
        case 1:
          originalConsole.debug("[tauri:trace]", message);
          break;
        case 2:
          originalConsole.debug("[tauri:debug]", message);
          break;
        case 3:
          originalConsole.info("[tauri:info]", message);
          break;
        case 4:
          originalConsole.warn("[tauri:warn]", message);
          break;
        case 5:
          originalConsole.error("[tauri:error]", message);
          break;
        default:
          originalConsole.log("[tauri]", message);
          break;
      }
    });

    const stringifyArgs = (args: unknown[]): string =>
      args
        .map((value) => {
          if (typeof value === "string") {
            return value;
          }

          try {
            return JSON.stringify(value);
          } catch {
            return String(value);
          }
        })
        .join(" ");

    console.log = (...args: unknown[]) => {
      originalConsole.log(...args);
      void trace(stringifyArgs(args));
    };
    console.debug = (...args: unknown[]) => {
      originalConsole.debug(...args);
      void debug(stringifyArgs(args));
    };
    console.info = (...args: unknown[]) => {
      originalConsole.info(...args);
      void info(stringifyArgs(args));
    };
    console.warn = (...args: unknown[]) => {
      originalConsole.warn(...args);
      void warn(stringifyArgs(args));
    };
    console.error = (...args: unknown[]) => {
      originalConsole.error(...args);
      void error(stringifyArgs(args));
    };

    await info("Frontend Tauri log bridge attached.");
  } catch (error) {
    console.warn("Failed to attach Tauri log console bridge", error);
  }
}

void attachTauriLogging();

createRoot(document.getElementById("root")!).render(<App />);
