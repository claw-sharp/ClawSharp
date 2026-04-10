import { test, expect } from "../src/playwright-fixture";

test.describe("Settings Provider Configuration", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await page.waitForSelector('button:has(svg.lucide-settings)');
    await page.locator('button:has(svg.lucide-settings)').click();

    const providerSelect = page.getByRole("combobox", { name: "Default Provider" });
    await expect(providerSelect).toBeVisible();
    await expect(providerSelect.locator("option")).toHaveCount(6);
  });

  test("should configure Anthropic API key", async ({ page }) => {
    const providerSelect = page.getByRole("combobox", { name: "Default Provider" });

    await expect(providerSelect).toHaveValue("anthropic");
    await page.getByLabel("Anthropic API Key").fill("sk-ant-test-key-12345");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(page.getByText("API key saved", { exact: true })).toBeVisible();
  });

  test("should switch to Codex and configure token", async ({ page }) => {
    const providerSelect = page.getByRole("combobox", { name: "Default Provider" });
    const modelSelect = page.getByRole("combobox", { name: "Default Model" });

    await providerSelect.selectOption("codex");
    await expect(modelSelect).toHaveValue("gpt-5.4");

    await expect(page.getByLabel("Use Codex auth file")).toBeVisible();
    await expect(page.getByLabel("Use saved access token and account ID")).toBeVisible();
    await page.getByLabel("Use saved access token and account ID").check();
    await expect(page.getByLabel("Codex Access Token")).toBeVisible();
    await expect(page.getByLabel("Codex Account ID")).toBeVisible();

    await page.getByLabel("Codex Access Token").fill("codex-token-xyz");
    await page.getByLabel("Codex Account ID").fill("my-org-id");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(providerSelect).toHaveValue("codex");
    await expect(page.getByText("Token and account ID saved", { exact: true })).toBeVisible();
  });

  test("should switch to OpenAI and verify model options update", async ({ page }) => {
    const providerSelect = page.getByRole("combobox", { name: "Default Provider" });
    const modelSelect = page.getByRole("combobox", { name: "Default Model" });

    await providerSelect.selectOption("openai");
    await expect(modelSelect).toHaveValue("gpt-4.1");
    await expect(modelSelect.locator("option")).toHaveText(["gpt-4.1", "o3"]);

    await page.getByLabel("OpenAI API Key").fill("sk-openai-test");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(providerSelect).toHaveValue("openai");
    await expect(page.getByText("API key saved", { exact: true })).toBeVisible();
  });
});
