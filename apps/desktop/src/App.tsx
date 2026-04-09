import { useEffect } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider, useTheme } from "next-themes";
import { HashRouter, Route, Routes } from "react-router-dom";
import { Toaster as Sonner } from "@/components/ui/sonner";
import { Toaster } from "@/components/ui/toaster";
import { TooltipProvider } from "@/components/ui/tooltip";
import { useAppStore } from "@/store";
import { toast } from "sonner";
import Index from "./pages/Index.tsx";
import NotFound from "./pages/NotFound.tsx";

const queryClient = new QueryClient();

const ThemeSync = () => {
  const theme = useAppStore((state) => state.settings.theme);
  const { setTheme } = useTheme();

  useEffect(() => {
    setTheme(theme);
  }, [setTheme, theme]);

  return null;
};

const UpdaterBootstrap = () => {
  useEffect(() => {
    if (typeof window === "undefined" || !("__TAURI_INTERNALS__" in window) || !import.meta.env.PROD) {
      return;
    }

    let cancelled = false;

    const installUpdate = async (update: { downloadAndInstall: () => Promise<void>; version: string }) => {
      try {
        await update.downloadAndInstall();
        if (!cancelled) {
          toast.success(`ClawSharp ${update.version} was installed. Restart the app to finish updating.`);
        }
      } catch (error) {
        if (!cancelled) {
          toast.error("Update installation failed. Check the desktop logs for details.");
        }
        console.warn("Failed to install desktop update", error);
      }
    };

    const checkForUpdates = async () => {
      try {
        const { check } = await import("@tauri-apps/plugin-updater");
        const update = await check();
        if (!update || cancelled) {
          return;
        }

        toast.info(`ClawSharp ${update.version} is available.`, {
          action: {
            label: "Install",
            onClick: () => {
              void installUpdate(update);
            },
          },
          duration: 12000,
        });
      } catch (error) {
        console.warn("Desktop updater check failed", error);
      }
    };

    void checkForUpdates();

    return () => {
      cancelled = true;
    };
  }, []);

  return null;
};

const App = () => (
  <ThemeProvider attribute="class" defaultTheme="dark" enableSystem disableTransitionOnChange>
    <ThemeSync />
    <UpdaterBootstrap />
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <Toaster />
        <Sonner />
        <HashRouter>
          <Routes>
            <Route path="/" element={<Index />} />
            {/* ADD ALL CUSTOM ROUTES ABOVE THE CATCH-ALL "*" ROUTE */}
            <Route path="*" element={<NotFound />} />
          </Routes>
        </HashRouter>
      </TooltipProvider>
    </QueryClientProvider>
  </ThemeProvider>
);

export default App;
