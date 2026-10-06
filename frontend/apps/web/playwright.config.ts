import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

import { API_URL, BASE_URL, REPO_ROOT, loadE2eEnv } from "./e2e/support/env";

/*
 * End-to-end suite (P3-09, skill auxilia-testing): real API + Postgres + the built web app. Prepare once with
 * `pnpm --filter web e2e:prepare` (Postgres from deploy/compose.dev.yml, backend built in Release, web app built),
 * then `pnpm --filter web e2e`. Playwright starts the API and `next start` unless they already run.
 */
loadE2eEnv();

const apiPort = new URL(API_URL).port || "80";
const webPort = new URL(BASE_URL).port || "80";

export default defineConfig({
  testDir: "./e2e",
  // Quality sweeps (H-02: Lighthouse budget and the light/dark axe sweep) take minutes: out of the default run and of
  // CI, run on demand with `pnpm --filter web e2e:quality` (E2E_QUALITY=1).
  testIgnore: process.env.E2E_QUALITY ? [] : ["**/lighthouse.spec.ts", "**/accessibility.spec.ts"],
  globalSetup: "./e2e/global-setup.ts",
  // Flows share the seeded users and sign-in rate limits: one worker, files in order, no retries hiding flakiness.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: Boolean(process.env.CI),
  timeout: 90_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]]
    : "list",
  use: {
    baseURL: BASE_URL,
    locale: "it-IT",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // A Chromium already on the machine (e.g. a cloud sandbox) instead of `playwright install`.
        launchOptions: process.env.E2E_CHROMIUM_PATH
          ? { executablePath: process.env.E2E_CHROMIUM_PATH }
          : {},
      },
    },
  ],
  webServer: [
    {
      name: "api",
      command:
        "dotnet run --project backend/src/Auxilia.Api -c Release --no-build --no-launch-profile",
      cwd: REPO_ROOT,
      url: `${API_URL}/health/live`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: `http://localhost:${apiPort}`,
        ConnectionStrings__Catalog: process.env.E2E_CATALOG_CONNECTION ?? "",
        ConnectionStrings__Redis: "",
        // Daily files under logs/ at the repository root (git-ignored): the console Log page reads them (S-07).
        AuxiliaLogging__Storage: "local-file",
        AuxiliaLogging__BatchPeriod: "00:00:00.200",
        // Every run activates an account and changes a password from the same address.
        RateLimiting__AccountLinks__PermitLimit: "100",
      },
    },
    {
      name: "web",
      command: `pnpm start --port ${webPort}`,
      cwd: path.resolve(__dirname),
      url: `${BASE_URL}/platform/login`,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
      env: {
        AUXILIA_API_URL: API_URL,
        AUXILIA_PUBLIC_ORIGIN: BASE_URL,
        // The browser connects to the API hub (SignalR, F16/F17).
        AUXILIA_API_PUBLIC_URL: API_URL,
        AUXILIA_WEB_CLIENT_ID: process.env.AUXILIA_WEB_CLIENT_ID ?? "",
        AUXILIA_WEB_CLIENT_SECRET: process.env.AUXILIA_WEB_CLIENT_SECRET ?? "",
        AUXILIA_CONSOLE_CLIENT_ID: process.env.AUXILIA_CONSOLE_CLIENT_ID ?? "",
        AUXILIA_CONSOLE_CLIENT_SECRET: process.env.AUXILIA_CONSOLE_CLIENT_SECRET ?? "",
      },
    },
  ],
});
