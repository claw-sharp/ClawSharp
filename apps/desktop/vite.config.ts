import { defineConfig } from "vite";
import react from "@vitejs/plugin-react-swc";
import path from "path";
import { componentTagger } from "lovable-tagger";

const tauriHost = process.env.TAURI_DEV_HOST;

export default defineConfig(({ mode }) => ({
  clearScreen: false,
  server: {
    host: tauriHost ?? "::",
    port: 1420,
    strictPort: true,
    hmr: tauriHost
      ? {
          protocol: "ws",
          host: tauriHost,
          port: 1421,
          overlay: false,
        }
      : {
          overlay: false,
        },
  },
  envPrefix: ["VITE_", "TAURI_ENV_*"],
  plugins: [react(), mode === "development" && componentTagger()].filter(Boolean),
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
    dedupe: ["react", "react-dom", "react/jsx-runtime", "react/jsx-dev-runtime", "@tanstack/react-query", "@tanstack/query-core"],
  },
  build: {
    outDir: "dist",
    sourcemap: Boolean(process.env.TAURI_ENV_DEBUG),
    minify: process.env.TAURI_ENV_DEBUG ? false : "esbuild",
    target: process.env.TAURI_ENV_PLATFORM === "windows" ? "chrome105" : "safari13",
  },
}));
