import { test, expect } from "../src/playwright-fixture";

test.describe("Settings Provider Configuration", () => {
  test.beforeEach(async ({ page }) => {
    // Advanced mock that tracks updates
    await page.addInitScript(() => {
      // @ts-ignore
      window.__MOCK_SETTINGS__ = {
        defaultProvider: 'anthropic',
        defaultModel: 'claude-3-opus-20240229',
        providerCredentials: {
          hasApiKey: false,
          hasAuthToken: false,
          accountId: null,
          source: 'none'
        },
        availableProviders: [
          { id: 'anthropic', displayName: 'Anthropic', models: ['claude-3-opus-20240229', 'claude-3-sonnet-20240229'] },
          { id: 'codex', displayName: 'Codex', models: ['codex-turbo'] },
          { id: 'openai', displayName: 'OpenAI', models: ['gpt-4-turbo'] }
        ]
      };

      // Force Tauri mode
      // @ts-ignore
      window.__TAURI_INTERNALS__ = { 
        invoke: async (cmd, args) => {
          const { command, payload } = args || {};
          if (cmd === "agent_host_request") {
            if (command === "getSettings") return { settings: window.__MOCK_SETTINGS__ };
            if (command === "listProviders") return { providers: window.__MOCK_SETTINGS__.availableProviders };
            if (command === "listRecentProjects") return { projects: [] };
            if (command === "listPendingApprovals") return { approvals: [] };
            if (command === "updateSettings") {
              // @ts-ignore
              window.__MOCK_SETTINGS__ = { ...window.__MOCK_SETTINGS__, ...payload };
              if (payload.providerApiKey || payload.providerAuthToken) {
                // @ts-ignore
                window.__MOCK_SETTINGS__.providerCredentials.hasApiKey = true;
                // @ts-ignore
                window.__MOCK_SETTINGS__.providerCredentials.source = 'saved';
              }
              // Return updated settings to match expected behavior
              return { settings: window.__MOCK_SETTINGS__ };
            }
          }
          return {};
        }
      };
      
      // Also mock window.__TAURI_INTERCEPT_INVOKE__ just in case
      // @ts-ignore
      window.__TAURI_INTERCEPT_INVOKE__ = window.__TAURI_INTERNALS__.invoke;
    });
    
    await page.goto("/");
    // Wait for the app to load and find the settings button
    await page.waitForSelector('button:has(svg.lucide-settings)');
    await page.locator('button:has(svg.lucide-settings)').click();
    // Wait for the dialog to be open
    await expect(page.getByText('Default Provider')).toBeVisible();
  });

  test("should configure Anthropic API key", async ({ page }) => {
    // 1. Verify we are on Anthropic by default
    const providerSelect = page.getByRole('combobox', { name: 'Default Provider' });
    await expect(providerSelect).toBeVisible();
    
    // Ensure options are loaded
    await expect(providerSelect.locator('option')).not.toHaveCount(0);
    await expect(providerSelect).toHaveValue('anthropic');
    
    // 2. Enter API Key
    const apiKeyLabel = 'Anthropic API Key';
    const apiKeyInput = page.getByLabel(apiKeyLabel);
    await apiKeyInput.fill('sk-ant-test-key-12345');
    
    // 3. Click Save Credentials
    await page.getByRole('button', { name: 'Save Credentials' }).click();
    
    // 4. Verify local UI反馈
    await expect(page.locator('text=API key saved')).toBeVisible();
  });

  test("should switch to Codex and configure Token", async ({ page }) => {
    // 1. Switch Provider
    const providerSelect = page.getByRole('combobox', { name: 'Default Provider' });
    await providerSelect.selectOption('codex');
    
    // 2. Verify Codex UI elements appear
    await expect(page.getByLabel('Codex Access Token')).toBeVisible();
    await expect(page.getByLabel('Codex Account ID')).toBeVisible();
    
    // 3. Fill details
    await page.getByLabel('Codex Access Token').fill('codex-token-xyz');
    await page.getByLabel('Codex Account ID').fill('my-org-id');
    
    // 4. Save
    await page.getByRole('button', { name: 'Save Credentials' }).click();
    
    // 5. Verify local UI反馈
    await expect(page.locator('text=Token and account ID saved')).toBeVisible();
  });

  test("should switch to OpenAI and verify model options update", async ({ page }) => {
    // 1. Switch to OpenAI
    const providerSelect = page.getByRole('combobox', { name: 'Default Provider' });
    await providerSelect.selectOption('openai');
    
    // 2. Verify model select has OpenAI models from mock
    const modelSelect = page.getByRole('combobox', { name: 'Default Model' });
    await expect(modelSelect).toContainText('gpt-4-turbo');
    
    // 3. Enter API Key
    await page.getByLabel('OpenAI API Key').fill('sk-openai-test');
    await page.getByRole('button', { name: 'Save Credentials' }).click();
    
    await expect(page.locator('text=API key saved')).toBeVisible();
  });
});
