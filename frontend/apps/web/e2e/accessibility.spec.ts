import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll } from "./support/ui";

/*
 * Accessibility sweep (H-02, WCAG 2.2 AA): every page of the tenant app an Administrator reaches, in the light and in
 * the dark theme (the functional specs check the light one on the pages they visit), and the 360 px layout of the
 * pages no functional spec visits. `elisa.moro` (Administrator) is owned by this spec.
 */
const TENANT_PAGES = [
  "dashboard",
  "clients?view=all",
  "clients/overview",
  "clients/new",
  "employees",
  "employees/new",
  "cases",
  "services",
  "documents",
  "appointments?view=calendar",
  "appointments?view=list",
  "tasks",
  "requests",
  "notifications",
  "sessions",
  "exports",
  "login-audit",
  "profile",
  "marketing/campaigns",
  "marketing/campaigns/new",
  "marketing/segments",
  "marketing/segments/new",
  "marketing/lists",
  "marketing/templates",
  "marketing/templates/new",
  "marketing/suppressions",
];

/** Pages without a functional spec: their 360 px layout is checked here. */
const NOT_COVERED_AT_360 = [
  "tasks",
  "sessions",
  "marketing/segments",
  "marketing/suppressions",
  "marketing/templates",
];

const PUBLIC_PAGES = ["login", "forgot-password", "reset-password", "activate"];

async function settle(page: Page, scheme: "light" | "dark") {
  await page.waitForLoadState("networkidle");
  // next-themes applies the theme as a class on <html>: the sweep really measures the requested colours.
  await expect(page.locator("html")).toHaveClass(
    scheme === "dark" ? /\bdark\b/ : /^(?!.*\bdark\b)/,
  );
  // Skeletons are placeholders: the page is measured with its content.
  await expect(page.locator('[data-slot="skeleton"]')).toHaveCount(0);
}

for (const scheme of ["light", "dark"] as const) {
  test(`public pages in the ${scheme} theme`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme });
    for (const path of PUBLIC_PAGES) {
      await page.goto(`/${E2E.tenant}/${path}`);
      await settle(page, scheme);
      await expectAccessible(page, `${path} (${scheme})`);
    }
  });
}

test("tenant pages of an Administrator in the light and dark themes", async ({ page }) => {
  test.setTimeout(300_000);
  await signInWithNewPassword(page, E2E.accessibilityUser.userName);

  for (const scheme of ["light", "dark"] as const) {
    // The profile theme is "System": the page follows the emulated preference.
    await page.emulateMedia({ colorScheme: scheme });
    for (const path of TENANT_PAGES) {
      await test.step(`${path} (${scheme})`, async () => {
        await page.goto(`/${E2E.tenant}/${path}`);
        await settle(page, scheme);
        await expectAccessible(page, `${path} (${scheme})`);
      });
    }
  }

  await page.setViewportSize({ width: 360, height: 780 });
  for (const path of NOT_COVERED_AT_360) {
    await test.step(`${path} at 360 px`, async () => {
      await page.goto(`/${E2E.tenant}/${path}`);
      await settle(page, "dark");
      await expectNoHorizontalScroll(page);
    });
  }
});
