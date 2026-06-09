import { expect, test } from "@playwright/test";

test("operator drives the reconcile / backfill path", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: /start your first migration/i }).click();

  // Step 1 — Mode: choose reconcile / backfill
  await page.getByRole("radio", { name: /reconcile/i }).click();
  await page.getByRole("button", { name: /continue/i }).click();

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

  // Scope (reconcile) — shows the Match by control and starts the reconcile (no Review step)
  await expect(page.getByText(/match by/i)).toBeVisible();
  await page.getByRole("button", { name: /start reconcile \/ repair/i }).click();

  // Reconcile Run view — folder-based progress + tiles fed by the fake SignalR hub
  await expect(page.getByRole("heading", { name: /reconciling/i })).toBeVisible();
  await expect(page.getByText(/folder 3 of/i)).toBeVisible();
  await expect(page.getByText("318")).toBeVisible();   // Copied
  await expect(page.getByText(/2,840/)).toBeVisible();  // Already-complete skipped
});
