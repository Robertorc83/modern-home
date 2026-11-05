import type { Page } from "@playwright/test";

/** Signs in with one of the demo accounts on the login page. */
export async function demoLogin(page: Page, role: "Agent" | "Buyer" | "Admin", tenant: string) {
  await page.goto("/login");
  await page.getByRole("button", { name: new RegExp(`^${role}\\s+${tenant}$`) }).click();
  await page.waitForURL((u) => !u.pathname.startsWith("/login"));
}
