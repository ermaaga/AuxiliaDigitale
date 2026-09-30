import { expect, test } from "@playwright/test";

import { E2E, required } from "./support/env";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Tenant sign-in (F01, F35): the public pages, the generic error, the temporary password that must be changed, the
 * shell with the navigation of the role, the login audit, remembered user name, sign-out and sign-in again.
 */
const login = `/${E2E.tenant}/login`;
// A new password on every run: the API refuses the last ones (password history).
const newPassword = `E2e-Pw-${Date.now()}!`;

test("public pages are accessible in light and dark mode", async ({ page }) => {
  await page.goto(login);
  await expect(page.getByRole("heading", { name: t("app.auth.login.title") })).toBeVisible();
  await expectAccessible(page, "login");

  await page.emulateMedia({ colorScheme: "dark" });
  await page.getByRole("link", { name: t("ForgotPassword") }).click();
  await expect(page).toHaveURL(`/${E2E.tenant}/forgot-password`);
  await expectAccessible(page, "forgot password (dark)");

  await page.goto(`/${E2E.tenant}/activate`);
  await expect(page.getByText(t("app.auth.linkMissing"))).toBeVisible();
});

test("an unknown tenant is not found", async ({ page }) => {
  await page.goto("/no-such-tenant/login");
  await expect(page.getByRole("heading", { name: t("app.shell.notFound.title") })).toBeVisible();
});

test("sign-in journey of a tenant Administrator", async ({ page }) => {
  const tempPassword = required("E2E_TEMP_PASSWORD");

  await test.step("a wrong password gets a generic error", async () => {
    await page.goto(login);
    await page.getByLabel(t("Username")).fill(E2E.userName);
    await page.getByLabel(t("Password"), { exact: true }).fill("not the password");
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page.locator("[data-slot=alert]")).toContainText(t("errors.AUX-12002"));
    await expect(page.getByTestId("error-code")).toHaveText("AUX-12002");
  });

  await test.step("the temporary password must be changed before the app opens", async () => {
    await page.getByLabel(t("Password"), { exact: true }).fill(tempPassword);
    await page.getByLabel(t("RememberUsername")).check();
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page).toHaveURL(/\/demo\/password-expired/);
    await expectAccessible(page, "password expired");

    await page.getByLabel(t("CurrentPassword")).fill(tempPassword);
    await page.getByLabel(t("NewPassword"), { exact: true }).fill(newPassword);
    await page.getByLabel(t("ConfirmNewPassword")).fill(newPassword);
    await page.getByRole("button", { name: t("ChangePassword") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);
  });

  await test.step("the shell shows the navigation of the roles", async () => {
    await expect(page.getByRole("heading", { name: new RegExp(t("Welcome")) })).toBeVisible();
    const navigation = page.getByRole("navigation", { name: t("app.shell.navigation") });
    await expect(navigation.getByRole("link", { name: t("nav.dashboard") })).toBeVisible();
    await expect(navigation.getByRole("link", { name: t("nav.loginAudit") })).toBeVisible();
    await expectAccessible(page, "dashboard");
  });

  await test.step("the login audit lists the attempts, failed ones included", async () => {
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.loginAudit") })
      .click();
    await expect(page).toHaveURL(`/${E2E.tenant}/login-audit`);
    const table = page.getByRole("table", { name: t("LoginAuditLog") });
    await expect(table.getByRole("row").nth(1)).toBeVisible();
    await expect(table).toContainText(t("app.identity.loginFailures.InvalidCredentials"));
    await expectAccessible(page, "login audit");
  });

  await test.step("on a phone the menu is a drawer and nothing scrolls sideways", async () => {
    await page.setViewportSize({ width: 360, height: 780 });
    await page.emulateMedia({ colorScheme: "dark" });
    await expectNoHorizontalScroll(page);
    await page.getByRole("button", { name: t("app.shell.openMenu") }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expectAccessible(page, "mobile menu (dark)");
    await page.keyboard.press("Escape");
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.emulateMedia({ colorScheme: "light" });
  });

  await test.step("sign-out keeps the remembered user name; the new password works", async () => {
    await page.getByRole("button", { name: t("app.shell.accountMenu") }).click();
    await page.getByRole("menuitem", { name: t("Logout") }).click();
    await expect(page).toHaveURL(login);
    await expect(page.getByLabel(t("Username"))).toHaveValue(E2E.userName);

    await page.goto(`/${E2E.tenant}/dashboard`);
    await expect(page).toHaveURL(login);

    await page.getByLabel(t("Password"), { exact: true }).fill(newPassword);
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);
  });

  await test.step("the tenant session does not open the platform console", async () => {
    await page.goto("/platform/tenants");
    await expect(page).toHaveURL(/\/platform\/login/);
  });
});
