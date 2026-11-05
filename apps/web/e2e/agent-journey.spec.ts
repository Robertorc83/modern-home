import { expect, test } from "@playwright/test";
import { demoLogin } from "./helpers";

test("agent signs in, sees their listings and opens the showing rules editor", async ({ page }) => {
  await demoLogin(page, "Agent", "Acme Realty");

  // The header always shows the brokerage and the role.
  await expect(page.getByRole("banner")).toContainText("Acme Realty");
  await expect(page.getByRole("banner")).toContainText("Agent");

  await page.goto("/agent/listings");
  const larimer = page.locator('a[href^="/agent/listings/"]').filter({ hasText: "Larimer" }).first();
  await expect(larimer).toBeVisible();
  await larimer.click();
  await page.waitForURL(/\/agent\/listings\/[^/]+$/);

  // The editor shows the rules and a live preview of the next open slots.
  await expect(page.getByText("Showing rules", { exact: true })).toBeVisible();
  await expect(page.getByText("Next open slots")).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Saturday window end" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save changes" })).toBeVisible();
});
