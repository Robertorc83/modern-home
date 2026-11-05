import { defineConfig, devices } from "@playwright/test";

// End-to-end journeys against a running stack (`docker compose up -d --build --wait` at the repo root).
// Set E2E_BASE_URL to point at another web instance.
export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    timezoneId: "America/Denver",
    trace: "retain-on-failure",
    ...devices["Desktop Chrome"],
  },
});
