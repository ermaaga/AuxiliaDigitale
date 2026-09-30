import { expect, test, type Page } from "@playwright/test";

import { E2E, required } from "./support/env";
import { nextTotpStep, totp } from "./support/totp";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Platform console (N02, D-21, D-22): activation of a System account (authenticator enrolment), sign-in with
 * password + TOTP, tenant list, tenant selector, tenant overview through the tenant-scoped platform token, sign-out.
 */
const password = `Console-Pw-${Date.now()}`;

async function signIn(page: Page, code: string) {
  await page.getByLabel(t("Email")).fill(E2E.systemEmail);
  await page.getByLabel(t("Password"), { exact: true }).fill(password);
  await page.getByLabel(t("app.platform.login.code")).fill(code);
  await page.getByRole("button", { name: t("Login") }).click();
}

test("console journey of a System user", async ({ page }) => {
  let secret = "";

  await test.step("signed out, the console asks to sign in", async () => {
    await page.goto("/platform");
    await expect(page).toHaveURL(/\/platform\/login/);
    await expect(page.getByRole("heading", { name: t("app.platform.login.title") })).toBeVisible();
    await expectAccessible(page, "console login");
  });

  await test.step("activation: enrol the authenticator, then password and first code", async () => {
    await page.getByRole("link", { name: t("app.platform.login.activateLink") }).click();
    await expect(page).toHaveURL(/\/platform\/activate/);

    await page.getByLabel(t("app.platform.activate.token")).fill("not-a-token");
    await page.getByRole("button", { name: t("app.platform.activate.start") }).click();
    await expect(page.getByTestId("error-code")).toHaveText("AUX-12018");

    await page.getByLabel(t("app.platform.activate.token")).fill(required("E2E_ACTIVATION_TOKEN"));
    await page.getByRole("button", { name: t("app.platform.activate.start") }).click();
    secret = (await page.getByTestId("totp-secret").innerText()).replace(/\s/g, "");
    expect(secret).toMatch(/^[A-Z2-7]{16,}$/);
    await expect(
      page.getByRole("link", { name: t("app.platform.activate.openApp") }),
    ).toHaveAttribute("href", /^otpauth:\/\/totp\//);

    await page.getByRole("button", { name: t("app.auth.activate.submit") }).click();
    await expect(page.getByText(t("validation.password.tooShort"))).toBeVisible();
    await expectAccessible(page, "activation with errors");

    await page.getByLabel(t("NewPassword"), { exact: true }).fill(password);
    await page.getByLabel(t("ConfirmPassword")).fill(password);
    await page.getByLabel(t("app.platform.login.code")).fill(totp(secret));
    await page.getByRole("button", { name: t("app.auth.activate.submit") }).click();
    await expect(page.getByText(t("app.auth.activate.success"))).toBeVisible();
  });

  await test.step("a wrong code is refused without saying which field was wrong", async () => {
    await page.getByRole("link", { name: t("Login") }).click();
    await expect(page).toHaveURL(/\/platform\/login/);
    await signIn(page, "000000");
    await expect(page.locator("[data-slot=alert]")).toContainText(t("app.platform.login.failed"));
  });

  await test.step("password and a fresh code open the tenant list", async () => {
    // The activation used the current step: the API refuses it again (replay protection).
    await nextTotpStep();
    await signIn(page, totp(secret));
    await expect(page).toHaveURL(/\/platform\/tenants$/);
    const table = page.getByRole("table", { name: t("app.platform.tenants.title") });
    await expect(table.getByRole("link", { name: "Demo" })).toBeVisible();
    await expect(table.getByRole("link", { name: "Beta Studio" })).toBeVisible();
    await expectAccessible(page, "tenant list");
  });

  await test.step("search and clear filters through the URL", async () => {
    await page.getByLabel(t("Search")).fill("studio");
    await expect(page).toHaveURL(/filter%5Bsearch%5D=studio|filter\[search\]=studio/);
    const table = page.getByRole("table", { name: t("app.platform.tenants.title") });
    await expect(table.getByRole("link", { name: "Demo" })).toHaveCount(0);
    await page.getByRole("button", { name: t("ClearFilters") }).click();
    await expect(table.getByRole("link", { name: "Demo" })).toBeVisible();
  });

  await test.step("the tenant selector opens a tenant; its languages come through the tenant token", async () => {
    const selector = page.getByRole("combobox", { name: t("app.platform.tenantSelector.label") });
    await selector.click();
    await page.getByRole("option", { name: /Demo/ }).click();
    await expect(page).toHaveURL(`/platform/tenants/${E2E.tenant}`);
    await expect(page.getByRole("heading", { name: "Demo", level: 1 })).toBeVisible();
    await expect(
      page.getByRole("heading", { name: t("app.platform.tenant.languages") }),
    ).toBeVisible();
    await expect(page.getByText("Italiano")).toBeVisible();
    await expectAccessible(page, "tenant overview");

    await selector.click();
    await page.getByRole("option", { name: /Beta Studio/ }).click();
    await expect(page).toHaveURL(`/platform/tenants/${E2E.otherTenant}`);
    await expect(page.getByRole("heading", { name: "Beta Studio", level: 1 })).toBeVisible();
  });

  await test.step("an unknown tenant is not found inside the console", async () => {
    await page.goto("/platform/tenants/no-such-tenant");
    await expect(page.getByRole("heading", { name: t("app.shell.notFound.title") })).toBeVisible();
  });

  await test.step("on a phone in dark mode the list fits the screen", async () => {
    await page.goto("/platform/tenants");
    await page.setViewportSize({ width: 360, height: 780 });
    await page.emulateMedia({ colorScheme: "dark" });
    await expect(page.getByRole("link", { name: "Demo" })).toBeVisible();
    await expectNoHorizontalScroll(page);
    await expectAccessible(page, "tenant list (mobile, dark)");
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step("the console session does not open the tenant app", async () => {
    await page.goto(`/${E2E.tenant}/dashboard`);
    await expect(page).toHaveURL(`/${E2E.tenant}/login`);
  });

  await test.step("sign-out ends the console session", async () => {
    await page.goto("/platform/tenants");
    await page.getByRole("button", { name: t("app.shell.accountMenu") }).click();
    await page.getByRole("menuitem", { name: t("Logout") }).click();
    await expect(page).toHaveURL(/\/platform\/login/);
    await page.goto("/platform/tenants");
    await expect(page).toHaveURL(/\/platform\/login/);
  });
});
