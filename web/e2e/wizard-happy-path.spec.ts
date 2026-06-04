import { expect, test } from "@playwright/test";

test("operator drives the wizard happy path", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: /start your first migration/i }).click();

  // From & To
  await page.getByRole("radio", { name: /from workmail/i }).click();
  await page.getByRole("radio", { name: /to microsoft 365/i }).click();
  await page.getByRole("button", { name: /continue/i }).click();

  // Connect From — test must pass to advance
  await page.getByLabel(/username/i).fill("old@biz.com");
  await page.getByLabel(/password/i).fill("app-pw");
  await page.getByRole("button", { name: /test connection/i }).click();
  await expect(page.getByText(/found 14 folders, 3,201 messages/i)).toBeVisible();
  await page.getByRole("button", { name: /continue/i }).click();

  // Connect To
  await expect(page.getByRole("heading", { name: /connect to/i })).toBeVisible();
  await page.getByRole("button", { name: /test connection/i }).click();
  await expect(page.getByText(/found 14 folders/i)).toBeVisible();
  await page.getByRole("button", { name: /continue/i }).click();

  // Scope (single) → Review
  await page.getByRole("button", { name: /continue/i }).click();
  await expect(page.getByText(/ready to migrate/i)).toBeVisible();
  await page.getByRole("button", { name: /start migration/i }).click();

  // Run
  await expect(page.getByRole("progressbar")).toBeVisible();
});
