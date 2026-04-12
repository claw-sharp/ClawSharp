import { useEffect } from 'react';
import { useAppStore } from '@/store';
import { TopBar } from '@/components/TopBar';
import { Sidebar } from '@/components/Sidebar';
import { NavigationLoadingDialog } from '@/components/NavigationLoadingDialog';
import { ThreadView } from '@/features/chat/ThreadView';
import { ReviewPanel } from '@/features/review/ReviewPanel';
import { BottomDrawer } from '@/features/logs/BottomDrawer';
import { InboxPanel } from '@/features/inbox/InboxPanel';
import { AutomationsPanel } from '@/features/automations/AutomationsPanel';
import { PluginsPanel } from '@/features/plugins/PluginsPanel';
import { SettingsDialog } from '@/features/settings/SettingsDialog';
import { CommandPalette } from '@/components/CommandPalette';
import { ResizableHandle, ResizablePanel, ResizablePanelGroup } from '@/components/ui/resizable';

export const AppShell = () => {
  const { ui, initialize, toggleLeftSidebar, toggleBottomDrawer, toggleCommandPalette } = useAppStore();

  // Keyboard shortcuts
  useEffect(() => {
    void initialize();
  }, [initialize]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.metaKey || e.ctrlKey) {
        if (e.key === 'b') { e.preventDefault(); toggleLeftSidebar(); }
        if (e.key === 'j') { e.preventDefault(); toggleBottomDrawer(); }
        if (e.key === 'k') { e.preventDefault(); toggleCommandPalette(); }
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [toggleLeftSidebar, toggleBottomDrawer, toggleCommandPalette]);

  const renderCenter = () => {
    switch (ui.activeView) {
      case 'inbox': return <InboxPanel />;
      case 'automations': return <AutomationsPanel />;
      case 'plugins': return <PluginsPanel />;
      default: return <ThreadView />;
    }
  };

  return (
    <div className="flex flex-col h-screen overflow-hidden surface-1">
      <TopBar />
      <div className="flex flex-1 overflow-hidden">
        {ui.leftSidebarCollapsed ? (
          <>
            <Sidebar />
            <div className="flex flex-1 flex-col overflow-hidden">
              <div className="flex flex-1 overflow-hidden">
                {ui.activeView === 'threads' ? (
                  <ResizablePanelGroup direction="horizontal">
                    <ResizablePanel defaultSize={72} minSize={45}>
                      <div className="flex h-full flex-col overflow-hidden">
                        {renderCenter()}
                      </div>
                    </ResizablePanel>
                    <ResizableHandle className="hidden lg:flex" />
                    <ResizablePanel defaultSize={28} minSize={20} maxSize={45} className="hidden lg:flex">
                      <ReviewPanel />
                    </ResizablePanel>
                  </ResizablePanelGroup>
                ) : (
                  <div className="flex-1 flex flex-col overflow-hidden">
                    {renderCenter()}
                  </div>
                )}
              </div>
              <div className="shrink-0" style={{ height: ui.bottomDrawerOpen ? '200px' : '36px' }}>
                <BottomDrawer />
              </div>
            </div>
          </>
        ) : (
          <>
            <Sidebar />
            <div className="flex flex-1 flex-col overflow-hidden">
              <div className="flex flex-1 overflow-hidden">
                {ui.activeView === 'threads' ? (
                  <ResizablePanelGroup direction="horizontal">
                    <ResizablePanel defaultSize={72} minSize={45}>
                      <div className="flex h-full flex-col overflow-hidden">
                        {renderCenter()}
                      </div>
                    </ResizablePanel>
                    <ResizableHandle className="hidden shrink-0 lg:flex" />
                    <ResizablePanel defaultSize={28} minSize={20} maxSize={45} className="hidden lg:flex">
                      <ReviewPanel />
                    </ResizablePanel>
                  </ResizablePanelGroup>
                ) : (
                  <div className="flex-1 flex flex-col overflow-hidden">
                    {renderCenter()}
                  </div>
                )}
              </div>
              <div className="shrink-0" style={{ height: ui.bottomDrawerOpen ? '200px' : '36px' }}>
                <BottomDrawer />
              </div>
            </div>
          </>
        )}
      </div>
      <SettingsDialog />
      <CommandPalette />
      <NavigationLoadingDialog />
    </div>
  );
};
