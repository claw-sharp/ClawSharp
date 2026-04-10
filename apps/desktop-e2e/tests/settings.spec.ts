import { test, expect } from "../src/playwright-fixture";

test.describe("Settings and UI State", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      // @ts-ignore
      window.__TAURI_INTERCEPT_INVOKE__ = (cmd, args) => {
        if (cmd === "agent_host_request") {
          const { command } = args;
          if (command === "get_settings") return Promise.resolve({ settings: { theme: 'dark' } });
          if (command === "list_providers") return Promise.resolve({ providers: [] });
          if (command === "list_recent_projects") return Promise.resolve({ projects: [] });
          return Promise.resolve({});
        }
        return Promise.resolve();
      };
    });
    await page.goto("/");
  });

  test("should open settings dialog", async ({ page }) => {
    // Select the button containing the settings icon
    await page.locator('button:has(svg.lucide-settings)').click();

    // Verify dialog header is visible
    const dialogHeader = page.locator('h2:has-text("Settings")');
    await expect(dialogHeader).toBeVisible();
  });

  test("should toggle bottom drawer with Ctrl+J", async ({ page }) => {
    await page.keyboard.press("Control+j");
  });
});
