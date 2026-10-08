import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";
import { expectAccessible, t } from "./support/ui";

/**
 * Parity regression per role (H-04, F22 and the legacy pages of each role): the menu has exactly the pages of the
 * role, every one opens without an error and passes axe, and the pages of the other roles stay closed. The spec owns
 * one user per role, so it runs again on the same database.
 */
const ROLES = [
  {
    name: "Administrator",
    user: E2E.parityAdministrator,
    pages: [
      "dashboard",
      "clients",
      "employees",
      "cases",
      "services",
      "appointments",
      "documents",
      "requests",
      "tasks",
      "marketing",
      "sessions",
      "loginAudit",
    ],
    closed: [],
  },
  {
    name: "Employee",
    user: E2E.parityEmployee,
    pages: [
      "dashboard",
      "clients",
      "cases",
      "appointments",
      "documents",
      "requests",
      "tasks",
      "marketing",
    ],
    closed: ["/employees", "/services", "/sessions", "/login-audit"],
  },
  {
    name: "Client",
    user: E2E.parityClient,
    pages: ["dashboard", "cases", "appointments", "requests"],
    closed: ["/clients", "/employees", "/documents", "/marketing", "/sessions"],
  },
] as const;

for (const role of ROLES) {
  test(`parity of the ${role.name} pages`, async ({ page }) => {
    test.setTimeout(120_000);
    await signInWithNewPassword(page, role.user.userName);
    const navigation = page.getByRole("navigation", { name: t("app.shell.navigation") });

    await test.step("the menu has exactly the pages of the role", async () => {
      const labels = await navigation.getByRole("link").allInnerTexts();
      expect(labels.map((label) => label.trim()).sort()).toEqual(
        role.pages.map((key) => t(`nav.${key}` as Parameters<typeof t>[0])).sort(),
      );
    });

    for (const key of role.pages) {
      await test.step(`${key} opens without errors`, async () => {
        await navigation
          .getByRole("link", { name: t(`nav.${key}` as Parameters<typeof t>[0]), exact: true })
          .click();
        await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
        await expectNoError(page);
        await expectAccessible(page, `${role.name} ${key}`);
      });
    }

    for (const path of role.closed) {
      await test.step(`${path} stays closed`, async () => {
        await page.goto(`/${E2E.tenant}${path}`);
        await expect(
          page.getByRole("heading", { name: t("app.shell.notFound.title") }),
        ).toBeVisible();
      });
    }
  });
}

/** No API error on the page (the alert of `ApiErrorAlert` carries the `AUX-` code). */
async function expectNoError(page: Page) {
  await page.waitForLoadState("networkidle");
  await expect(page.getByTestId("error-code")).toHaveCount(0);
}
