import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/site",
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: true,
  workers: process.env.CI ? 2 : 4,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "artifacts/playwright-report" }]],
  outputDir: "artifacts/playwright-results",
  use: {
    baseURL: "http://127.0.0.1:4173/LearnDotnetCSharp/",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    { name: "mobile", use: { ...devices["Pixel 7"], defaultBrowserType: "chromium" } },
  ],
  webServer: {
    command: "node scripts/serve-study-site.mjs 4173 artifacts/study-site /LearnDotnetCSharp/",
    url: "http://127.0.0.1:4173/LearnDotnetCSharp/",
    reuseExistingServer: false,
  },
});
