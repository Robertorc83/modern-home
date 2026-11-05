import { expect, test } from "@playwright/test";
import { demoLogin } from "./helpers";

test("buyer describes their week in words, searches near LoDo and gets a slot hold", async ({ page }) => {
  await demoLogin(page, "Buyer", "Acme Realty");

  // Free text -> windows (Claude Haiku when a key is set, the rule-based parser otherwise).
  await page.getByRole("tab", { name: "Describe it" }).click();
  await page.getByRole("textbox").first().fill("weekday evenings after 6 except Thursday, Saturday morning");
  await page.getByRole("button", { name: "Parse" }).click();
  await expect(page.getByText("Understood as")).toBeVisible();
  await expect(page.getByText("Review the dates")).toBeVisible();

  await page.getByLabel("Near").fill("LoDo");
  await page.getByRole("button", { name: "Search with these" }).click();
  const results = page.getByRole("region", { name: "Results" });
  await expect(results).toContainText(/homes? with open slots in your times/);
  const first = results.locator('a[href^="/buyer/listings/"]').first();
  await expect(first).toBeVisible();

  // Opening a slot holds it for this buyer for a few minutes (TTL hold); closing the dialog releases it.
  await first.click();
  await page.waitForURL(/\/buyer\/listings\/[^/]+$/);
  await page.getByRole("button", { name: /\d{1,2}:\d{2}/ }).first().click();
  await expect(page.getByText(/Held for you/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Confirm booking" })).toBeVisible();
  await page.keyboard.press("Escape");
});

test("a buyer from another brokerage cannot see an Acme listing (404, not 403)", async ({ browser, page }) => {
  // Take a real Acme listing URL as an Acme buyer...
  await demoLogin(page, "Buyer", "Acme Realty");
  await page.getByLabel("Near").fill("LoDo");
  await page.getByRole("button", { name: "Find showings" }).click();
  const href = await page
    .getByRole("region", { name: "Results" })
    .locator('a[href^="/buyer/listings/"]')
    .first()
    .getAttribute("href");
  expect(href).toBeTruthy();

  // ...and paste it as a Northstar buyer: the tenant comes from the token, so the id does not exist for them.
  const other = await browser.newContext();
  const otherPage = await other.newPage();
  await demoLogin(otherPage, "Buyer", "Northstar Homes");
  await otherPage.goto(href!);
  await expect(otherPage.getByText(/not found/i).first()).toBeVisible();
  await other.close();
});
