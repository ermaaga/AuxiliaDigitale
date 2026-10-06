import { mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";

import { chromium, expect, test, type BrowserContext } from "@playwright/test";
import lighthouse from "lighthouse";
import desktopConfig from "lighthouse/core/config/desktop-config.js";

import { BASE_URL, E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";

/*
 * Lighthouse budget (H-02): performance, accessibility and best practices ≥ 90 on the key pages of the production
 * build (`next start`). Desktop preset: the tenant app is a back office used at the office desk; the 360 px layout is
 * covered by the functional specs. Lighthouse drives the same Chromium through its debugging port, so the pages run
 * with the session of `luca.fabbri` (Administrator, owned by this spec).
 */
const MIN_SCORE = 0.9;
const CATEGORIES = ["performance", "accessibility", "best-practices"] as const;
const PORT = 9333;

const PAGES: { name: string; path: string; signedIn: boolean }[] = [
  { name: "login", path: `/${E2E.tenant}/login`, signedIn: false },
  { name: "console login", path: "/platform/login", signedIn: false },
  { name: "dashboard", path: `/${E2E.tenant}/dashboard`, signedIn: true },
  { name: "clients", path: `/${E2E.tenant}/clients?view=all`, signedIn: true },
  { name: "cases", path: `/${E2E.tenant}/cases`, signedIn: true },
  { name: "documents", path: `/${E2E.tenant}/documents`, signedIn: true },
  { name: "appointments", path: `/${E2E.tenant}/appointments`, signedIn: true },
  { name: "tasks", path: `/${E2E.tenant}/tasks`, signedIn: true },
];

test.describe.configure({ mode: "serial" });

let context: BrowserContext;
let profile: string;

test.beforeAll(async () => {
  profile = mkdtempSync(path.join(os.tmpdir(), "auxilia-lighthouse-"));
  context = await chromium.launchPersistentContext(profile, {
    baseURL: BASE_URL,
    locale: "it-IT",
    executablePath: process.env.E2E_CHROMIUM_PATH || undefined,
    args: [`--remote-debugging-port=${PORT}`],
  });
});

test.afterAll(async () => {
  await context?.close();
  rmSync(profile, { recursive: true, force: true });
});

test("anonymous pages", async ({}, testInfo) => {
  for (const page of PAGES.filter((item) => !item.signedIn)) {
    await audit(page.name, page.path, testInfo);
  }
});

test("pages of a signed-in Administrator", async ({}, testInfo) => {
  test.setTimeout(300_000);
  const page = await context.newPage();
  await signInWithNewPassword(page, E2E.lighthouseUser.userName);
  await page.close();

  for (const item of PAGES.filter((entry) => entry.signedIn)) {
    await audit(item.name, item.path, testInfo);
  }
});

async function audit(
  name: string,
  pagePath: string,
  testInfo: {
    attach: (name: string, options: { body: string; contentType: string }) => Promise<void>;
  },
) {
  const result = await lighthouse(
    new URL(pagePath, BASE_URL).toString(),
    {
      port: PORT,
      output: "json",
      logLevel: "error",
      onlyCategories: [...CATEGORIES],
      // Keep the session cookie of the signed-in user. (Error reporting to Sentry exists only in the Lighthouse CLI.)
      disableStorageReset: true,
    },
    desktopConfig,
  );
  expect(result, `Lighthouse ran on ${name}`).toBeDefined();
  const { lhr } = result!;
  const scores = Object.fromEntries(
    CATEGORIES.map((category) => [category, lhr.categories[category]?.score ?? 0]),
  );
  const failing = Object.values(lhr.audits)
    .filter(
      (item) => item.score !== null && item.score < 0.9 && item.scoreDisplayMode !== "informative",
    )
    .map((item) => `${item.id} (${item.score})${detailOf(item.details)}`);
  await testInfo.attach(`lighthouse-${name}.json`, {
    body: JSON.stringify({ url: lhr.finalDisplayedUrl, scores, failing }, null, 2),
    contentType: "application/json",
  });
  console.log(`lighthouse ${name}: ${JSON.stringify(scores)} — below 0.9: ${failing.join(", ")}`);
  expect(lhr.finalDisplayedUrl, `${name} was not redirected`).toContain(pagePath.split("?")[0]);
  for (const category of CATEGORIES) {
    expect(scores[category], `${category} of ${name}`).toBeGreaterThanOrEqual(MIN_SCORE);
  }
}

/** The first offending elements or messages of an audit (node snippets, console texts, issue types). */
function detailOf(details: unknown): string {
  const items = (details as { items?: Record<string, unknown>[] } | undefined)?.items ?? [];
  const texts = items.slice(0, 3).map((item) => {
    const node = item.node as { snippet?: string } | undefined;
    const subItems =
      (item.subItems as { items?: Record<string, unknown>[] } | undefined)?.items ?? [];
    return (
      node?.snippet ??
      (item.description as string | undefined) ??
      (subItems.length > 0
        ? `${String(item.issueType)}: ${JSON.stringify(subItems[0])}`
        : JSON.stringify(item))
    ).slice(0, 300);
  });
  return texts.length > 0 ? ` [${texts.join(" | ")}]` : "";
}
