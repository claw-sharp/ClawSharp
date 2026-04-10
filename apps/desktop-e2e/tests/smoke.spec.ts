import { test, expect } from "../src/playwright-fixture";

test.describe("ClawSharp Desktop Smoke Test", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      // @ts-ignore
      window.__TAURI_INTERCEPT_INVOKE__ = (cmd, args) => {
        if (cmd === "agent_host_request") {
          const { command } = args;
          if (command === "list_recent_projects") return Promise.resolve({ projects: [] });
          if (command === "list_providers") return Promise.resolve({ providers: [{ id: "anthropic", name: "Anthropic" }] });
          if (command === "list_pending_approvals") return Promise.resolve({ approvals: [] });
          return Promise.resolve({});
        }
        return Promise.resolve();
      };
    });
  });

  test("should load the app and show the TopBar", async ({ page }) => {
    await page.goto("/");
    // TopBar has ClawSharp wordmark
    await expect(page.locator('img[alt="ClawSharp"]')).toBeVisible();
    // Sidebar has "Projects" header
    await expect(page.getByText('Projects', { exact: true })).toBeVisible();
  });

  test("should update view when clicking sidebar icons", async ({ page }) => {
    await page.goto("/");
    // Click Inbox button in sidebar (exact match)
    await page.getByRole('button', { name: 'Inbox', exact: true }).click();
    // Verify Inbox panel is shown
    await expect(page.locator('h2:has-text("Inbox")')).toBeVisible();
    
    // Click Automations button in sidebar (exact match)
    await page.getByRole('button', { name: 'Automations', exact: true }).click();
    // Verify Automations panel is shown
    await expect(page.locator('h2:has-text("Automations")')).toBeVisible();
  });
});
