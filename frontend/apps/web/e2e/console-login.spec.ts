import { expect, test, type Page } from "@playwright/test";

import { E2E, required } from "./support/env";
import { nextTotpStep, totp } from "./support/totp";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Platform console (N02, D-21, D-22): activation of a System account (authenticator enrolment), sign-in with
 * password + TOTP, tenant list, tenant selector, tenant overview through the tenant-scoped platform token, settings
 * and branding of a tenant (S-02) seen on its sign-in page, sign-out.
 */
const password = `Console-Pw-${Date.now()}`;
const newSlug = `e2e-${Date.now().toString(36)}`;
/** A 1×1 PNG (the API recognises images by their signature). */
const PNG =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

async function signIn(page: Page, code: string) {
  await page.getByLabel(t("Email")).fill(E2E.systemEmail);
  await page.getByLabel(t("Password"), { exact: true }).fill(password);
  await page.getByLabel(t("app.platform.login.code")).fill(code);
  await page.getByRole("button", { name: t("Login") }).click();
}

test("console journey of a System user", async ({ page }) => {
  test.setTimeout(180_000);
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

  await test.step("a new tenant is created and waits for its provisioning", async () => {
    await page.goto("/platform/tenants");
    await page.getByRole("link", { name: t("app.platform.tenants.new") }).click();
    await expect(page).toHaveURL(/\/platform\/tenants\/new$/);
    await page.getByRole("button", { name: t("app.platform.create.submit") }).click();
    await expect(page.getByText(t("validation.tenant.slug"))).toBeVisible();
    await expectAccessible(page, "new tenant (errors)");

    await page.getByLabel(t("app.platform.tenants.slug")).fill(newSlug);
    await page.getByLabel(t("app.platform.tenants.name")).fill("Studio E2E");
    await page.getByLabel(t("Email")).fill("anna@e2e.test");
    await page.getByLabel(t("FirstName"), { exact: true }).fill("Anna");
    await page.getByLabel(t("LastName"), { exact: true }).fill("Rossi");
    await page.getByRole("button", { name: t("app.platform.create.submit") }).click();
    await expect(page).toHaveURL(`/platform/tenants/${newSlug}`);
    await expect(page.getByRole("heading", { name: "Studio E2E", level: 1 })).toBeVisible();
    // No Worker in this environment: the tenant stays in Provisioning and can be queued again.
    await expect(page.getByText(t("app.platform.tenant.provisioning"))).toBeVisible();
    await expect(page.getByRole("button", { name: t("app.platform.tenant.retry") })).toBeVisible();
    await expectAccessible(page, "tenant being provisioned");
  });

  await test.step("the tenant is edited, then archived after a confirmation", async () => {
    await page
      .getByRole("button", { name: t("Edit") })
      .first()
      .click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(t("app.platform.tenants.name")).fill("Studio E2E Bis");
    await expectAccessible(page, "edit tenant dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByRole("heading", { name: "Studio E2E Bis", level: 1 })).toBeVisible();

    await page.getByRole("button", { name: t("app.platform.tenant.archive") }).click();
    const confirm = page.getByRole("alertdialog");
    await expect(confirm).toContainText("Studio E2E Bis");
    await confirm.getByRole("button", { name: t("app.platform.tenant.archive") }).click();
    await expect(page.getByText(t("app.platform.tenant.archivedNotice"))).toBeVisible();
    await expect(page.getByRole("button", { name: t("app.platform.tenant.archive") })).toHaveCount(
      0,
    );
  });

  await test.step("a module override changes who sees the module, and goes back to the plan", async () => {
    await page.goto(`/platform/tenants/${E2E.otherTenant}`);
    const modules = page.getByRole("table", { name: t("app.platform.modules.title") });
    const cases = modules.getByRole("row").filter({ hasText: t("modules.cases.name") });
    await cases
      .getByRole("button", {
        name: t("app.platform.modules.edit", { module: t("modules.cases.name") }),
      })
      .click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("combobox").click();
    await page.getByRole("option", { name: t("app.platform.modules.disabled") }).click();
    await expectAccessible(page, "module override dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(cases).toContainText(t("app.platform.modules.disabled"));

    await cases
      .getByRole("button", {
        name: t("app.platform.modules.edit", { module: t("modules.cases.name") }),
      })
      .click();
    await page.getByRole("dialog").getByRole("combobox").click();
    await page
      .getByRole("option", { name: new RegExp(t("app.platform.modules.followPlan")) })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: t("Save") })
      .click();
    await expect(
      cases.getByRole("cell", { name: t("app.platform.modules.noOverride"), exact: true }),
    ).toBeVisible();
  });

  await test.step("the first Administrator is invited by e-mail only, pending without a sending account", async () => {
    const card = page
      .getByRole("heading", { name: t("app.platform.admins.title") })
      .locator("xpath=ancestor::*[@data-slot='card'][1]");
    const create = card.getByRole("button", { name: t("app.platform.admins.create") });
    // The first run creates the Administrator of beta; later runs find it.
    await expect(create.or(card.getByText(t("app.platform.admins.pending")))).toBeVisible();
    if (await create.isVisible()) {
      await card.getByLabel(t("Email")).fill("first.admin@beta.test");
      await card.getByLabel(t("FirstName"), { exact: true }).fill("Bea");
      await card.getByLabel(t("LastName"), { exact: true }).fill("Bianchi");
      await create.click();
      await expect(card.getByRole("status")).toContainText("AUX-25011");
    }

    await expect(card.getByText(t("app.platform.admins.pending"))).toBeVisible();
    await expectAccessible(page, "tenant with administrators");
  });

  await test.step("a setting is changed, refused when invalid, and restored", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/settings`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.settings.title"), level: 1 }),
    ).toBeVisible();
    const label = t("settings.cases.expiry.expiringDays.description");
    const days = page.getByLabel(label, { exact: true });
    const save = page.getByRole("button", {
      name: t("app.platform.settings.saveSetting", { setting: label }),
    });
    await days.fill("0");
    await save.click();
    await expect(page.getByText(t("validation.settings.valueInvalid"))).toBeVisible();
    await expectAccessible(page, "settings with an error");

    await days.fill("21");
    await save.click();
    await expect(page.getByText(t("app.platform.settings.saved"))).toBeVisible();
    const restore = page.getByRole("button", {
      name: t("app.platform.settings.resetSetting", { setting: label }),
    });
    await restore.click();
    await expect(page.getByText(t("app.platform.settings.restored"))).toBeVisible();
    await expect(days).toHaveValue("7");
    await expect(restore).toHaveCount(0);
  });

  await test.step("branding: app name, logo and login background reach the tenant sign-in", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/branding`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.branding.title"), level: 1 }),
    ).toBeVisible();
    await expect(page.getByTestId("branding-preview")).toBeVisible();
    // Before any toast: a stacked toast fading out is not part of the page.
    await expectAccessible(page, "branding editor");

    await page.getByLabel(t("app.platform.branding.logo"), { exact: true }).setInputFiles({
      name: "logo.svg",
      mimeType: "image/svg+xml",
      buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"/>'),
    });
    await expect(page.getByText(t("validation.branding.imageType"))).toBeVisible();
    await page.getByLabel(t("app.platform.branding.logo"), { exact: true }).setInputFiles({
      name: "logo.png",
      mimeType: "image/png",
      buffer: Buffer.from(PNG, "base64"),
    });
    await expect(page.getByText(t("app.platform.branding.imageSaved"))).toBeVisible();
    await expect(page.getByRole("img", { name: t("app.platform.branding.logo") })).toBeVisible();

    await page.getByLabel(t("app.platform.branding.appName"), { exact: true }).fill("Studio Demo");
    await page.getByLabel(t("app.platform.branding.useAppName"), { exact: true }).click();
    await page.getByLabel(t("app.platform.branding.backgroundKind"), { exact: true }).click();
    await page.getByRole("option", { name: t("app.platform.branding.kind.Solid") }).click();
    await page.getByLabel(t("app.platform.branding.color"), { exact: true }).fill("#1F4E79");
    await page.getByLabel(t("app.platform.branding.primaryColor"), { exact: true }).fill("nope");
    await page.getByRole("button", { name: t("Save"), exact: true }).click();
    await expect(page.getByText(t("validation.branding.color"))).toBeVisible();
    await page.getByLabel(t("app.platform.branding.primaryColor"), { exact: true }).fill("#2b6cb0");
    await page.getByRole("button", { name: t("Save"), exact: true }).click();
    await expect(page.getByText(t("app.platform.branding.saved"))).toBeVisible();

    await page.goto(`/${E2E.tenant}/login`);
    await expect(page.getByRole("img", { name: "Studio Demo" })).toBeVisible();
    await expect(page.getByTestId("login-background")).toHaveCSS(
      "background-color",
      "rgb(31, 78, 121)",
    );
    await expectAccessible(page, "branded tenant sign-in");
  });

  await test.step("branding goes back to the name and the gradient", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/branding`);
    await page
      .getByRole("button", {
        name: t("app.platform.branding.removeImage", { image: t("app.platform.branding.logo") }),
      })
      .click();
    await expect(page.getByText(t("app.platform.branding.imageRemoved"))).toBeVisible();
    await page.getByLabel(t("app.platform.branding.useAppName"), { exact: true }).click();
    await page.getByLabel(t("app.platform.branding.backgroundKind"), { exact: true }).click();
    await page.getByRole("option", { name: t("app.platform.branding.kind.Gradient") }).click();
    await page.getByLabel(t("app.platform.branding.primaryColor"), { exact: true }).fill("#667eea");
    await page.getByRole("button", { name: t("Save"), exact: true }).click();
    await expect(page.getByText(t("app.platform.branding.saved"))).toBeVisible();

    await page.goto(`/${E2E.tenant}/login`);
    await expect(page.getByText("Studio Demo").first()).toBeVisible();
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
