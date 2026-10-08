import { readFile } from "node:fs/promises";

import { expect, test, type Page } from "@playwright/test";

import { E2E, required } from "./support/env";
import { nextTotpStep, totp } from "./support/totp";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Platform console (N02, D-21, D-22): activation of a System account (authenticator enrolment), sign-in with
 * password + TOTP, tenant list, tenant selector, tenant overview through the tenant-scoped platform token, settings
 * and branding of a tenant (S-02) seen on its sign-in page, messaging accounts and rules (S-03),
 * custom fields and grid layouts (S-04), translations (S-05), role permissions and specializations (S-06), logs and
 * temporary debug level (S-07), imports (S-08), sign-out.
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
    // The QR code of the otpauth URI, to scan with the authenticator app; the key stays for manual entry.
    await expect(page.getByRole("img", { name: t("app.platform.activate.qrLabel") })).toBeVisible();
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

  await test.step("messaging: an SMTP account, a test send that fails, a rule and the log", async () => {
    const accountName = `Ufficio E2E ${Date.now().toString(36)}`;
    await page.goto(`/platform/tenants/${E2E.tenant}/messaging`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.messaging.title"), level: 1 }),
    ).toBeVisible();
    await expect(
      page.getByRole("table", { name: t("app.platform.messaging.accounts") }),
    ).toBeVisible();
    await expectAccessible(page, "messaging page");

    await page.getByRole("button", { name: t("app.platform.messaging.newAccount") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(dialog.getByText(t("validation.messaging.host"))).toBeVisible();
    await dialog.getByLabel(t("app.platform.messaging.accountName")).fill(accountName);
    await dialog.getByLabel(t("app.platform.messaging.host")).fill("127.0.0.1");
    // Nothing listens on port 1: the test send fails fast with "server not reachable".
    await dialog.getByLabel(t("app.platform.messaging.port")).fill("1");
    await dialog.getByLabel(t("app.platform.messaging.security")).click();
    await page
      .getByRole("option", { name: t("app.platform.messaging.securityOption.None") })
      .click();
    await dialog.getByLabel(t("Password")).fill("smtp-password");
    await dialog.getByLabel(t("app.platform.messaging.fromAddress")).fill("office@demo.test");
    await expectAccessible(page, "new SMTP account dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("app.platform.messaging.accountSaved"))).toBeVisible();
    const accounts = page.getByRole("table", { name: t("app.platform.messaging.accounts") });
    await expect(accounts.getByRole("row").filter({ hasText: accountName })).toBeVisible();

    await page
      .getByRole("button", {
        name: t("app.platform.messaging.testNamed", { account: accountName }),
      })
      .click();
    const test = page.getByRole("dialog");
    await test.getByLabel(t("app.platform.messaging.recipient")).fill("anna@example.test");
    await test.getByRole("button", { name: t("app.platform.messaging.sendTest") }).click();
    await expect(test.getByRole("status")).toContainText("AUX-25022");
    await expectAccessible(page, "test send outcome");
    await test
      .getByRole("button", { name: t("Close") })
      .first()
      .click();

    await page.getByRole("button", { name: t("app.platform.messaging.addRule") }).click();
    await page.getByRole("button", { name: t("app.platform.messaging.saveRules") }).click();
    await expect(page.getByText(t("app.platform.messaging.rulesSaved"))).toBeVisible();

    const log = page.getByRole("table", { name: t("app.platform.messaging.log") });
    await expect(
      log.getByRole("row").filter({ hasText: "anna@example.test" }).first(),
    ).toContainText(t("app.platform.messaging.status.Failed"));
  });

  await test.step("custom fields: a grouped yes/no counter field is created, edited and deleted", async () => {
    const key = `CAF${Date.now().toString(36)}`;
    await page.goto(`/platform/tenants/${E2E.tenant}/custom-fields`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.customFields.title"), level: 1 }),
    ).toBeVisible();
    await expect(page.getByLabel(t("app.platform.customFields.entity"))).toContainText(
      t("customFields.entities.client"),
    );
    await expectAccessible(page, "custom fields");

    await page.getByRole("button", { name: t("app.platform.customFields.new") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(t("app.platform.customFields.key"), { exact: true }).fill("1abc");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(dialog.getByText(t("validation.customFields.key"))).toBeVisible();
    await dialog.getByLabel(t("app.platform.customFields.key"), { exact: true }).fill(key);
    await dialog.getByLabel(t("app.platform.customFields.label"), { exact: true }).fill("CAF");
    await dialog.getByLabel(t("app.platform.customFields.type"), { exact: true }).click();
    await page.getByRole("option", { name: t("app.platform.customFields.types.Boolean") }).click();
    await dialog.getByLabel(t("app.platform.customFields.group"), { exact: true }).fill("Area");
    await dialog.getByLabel(t("app.platform.customFields.badgeColor")).fill("#72fa29");
    await dialog.getByLabel(t("app.platform.customFields.onGridLabel")).click();
    await dialog.getByLabel(t("app.platform.customFields.counterLabel")).click();
    await expectAccessible(page, "new custom field dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("app.platform.customFields.saved"))).toBeVisible();

    const table = page.getByRole("table", { name: t("app.platform.customFields.fieldsOf") });
    const row = table.getByRole("row").filter({ hasText: key });
    await expect(row).toContainText("Area");
    await expect(row).toContainText(t("app.platform.customFields.counter"));

    await page
      .getByRole("button", { name: t("app.platform.customFields.editNamed", { field: "CAF" }) })
      .first()
      .click();
    await page
      .getByRole("dialog")
      .getByLabel(t("app.platform.customFields.label"), { exact: true })
      .fill("CAF servizi");
    await page
      .getByRole("dialog")
      .getByRole("button", { name: t("Save") })
      .click();
    await expect(row).toContainText("CAF servizi");

    await page
      .getByRole("button", {
        name: t("app.platform.customFields.deleteNamed", { field: "CAF servizi" }),
      })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(page.getByText(t("app.platform.customFields.deleted"))).toBeVisible();
    await expect(row).toHaveCount(0);
  });

  await test.step("grids: the login audit columns of the Administrator are changed and reset", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/grids`);
    const name = t("grids.identity.loginAttempts.name");
    await expect(page.getByRole("heading", { name, level: 2 })).toBeVisible();
    await expectAccessible(page, "grids");
    // Other grids repeat column names (directory.clients has "Username" too): act inside this grid only.
    const grid = page.getByRole("region", { name });

    const ipAddress = grid.getByRole("checkbox", {
      name: t("app.identity.loginAttempts.ipAddress"),
    });
    await ipAddress.click();
    await grid
      .getByRole("button", { name: t("app.platform.grids.moveUp", { column: t("Username") }) })
      .click();
    await grid.getByRole("button", { name: t("Save"), exact: true }).click();
    await expect(page.getByText(t("app.platform.grids.saved"))).toBeVisible();
    await expect(grid.getByText(t("app.platform.grids.customized"))).toBeVisible();
    const columns = grid.getByRole("list", {
      name: t("app.platform.grids.columnsOf", { grid: name, role: t("Administrator") }),
    });
    await expect(columns.getByRole("listitem").first()).toContainText(t("Username"));
    await expect(ipAddress).not.toBeChecked();

    await grid.getByRole("button", { name: t("app.platform.grids.reset") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.platform.grids.reset") })
      .click();
    await expect(page.getByText(t("app.platform.grids.resetDone"))).toBeVisible();
    await expect(grid.getByText(t("app.platform.grids.default"), { exact: true })).toBeVisible();
    await expect(columns.getByRole("listitem").first()).toContainText(t("Date"));
  });

  await test.step("translations: a new key is added in Italian, translated in English in place and deleted", async () => {
    const key = `app.e2e.k${Date.now().toString(36)}`;
    await page.goto(`/platform/tenants/${E2E.tenant}/localization`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.localization.title"), level: 1 }),
    ).toBeVisible();
    await expect(
      page.getByRole("table", { name: t("app.platform.localization.keys") }),
    ).toBeVisible();
    await expectAccessible(page, "translations");

    await page.getByRole("button", { name: t("app.platform.localization.newKey") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(t("Key"), { exact: true }).fill("1 bad");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(dialog.getByText(t("validation.localization.key"))).toBeVisible();
    await dialog.getByLabel(t("Key"), { exact: true }).fill(key);
    await dialog.getByLabel("Italiano").fill("Prova E2E");
    await expectAccessible(page, "new key dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("app.platform.localization.created"))).toBeVisible();

    await page.getByLabel(t("Search")).fill(key);
    const table = page.getByRole("table", { name: t("app.platform.localization.keys") });
    const row = table.getByRole("row").filter({ hasText: key });
    await expect(row).toContainText("Prova E2E");
    await expect(row).toContainText(t("app.platform.localization.missing"));

    await row
      .getByRole("button", {
        name: t("app.platform.localization.addTranslation", { key, language: "English" }),
      })
      .click();
    await row
      .getByLabel(t("app.platform.localization.translationOf", { key, language: "English" }))
      .fill("E2E test");
    await expectAccessible(page, "inline translation");
    await row.getByRole("button", { name: t("Save") }).click();
    await expect(row).toContainText("E2E test");
    await expect(row).not.toContainText(t("app.platform.localization.missing"));

    await row
      .getByRole("button", { name: t("app.platform.localization.deleteKey", { key }) })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(page.getByText(t("app.platform.localization.deleted"))).toBeVisible();
    await expect(row).toHaveCount(0);
  });

  await test.step("permissions: Clients get a permission, then the defaults back", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/permissions`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.permissions.title"), level: 1 }),
    ).toBeVisible();
    await expectAccessible(page, "permissions");

    const sessions = page.getByRole("checkbox", {
      name: t("app.platform.permissions.toggle", {
        permission: t("permissions.identity.sessions.view.description"),
        role: t("Client"),
      }),
    });
    const resetClient = page.getByRole("button", {
      name: t("app.platform.permissions.resetNamed", { role: t("Client") }),
    });
    const customized = page.getByText(t("app.platform.permissions.customized"), { exact: true });
    async function restoreClient() {
      await resetClient.click();
      await page
        .getByRole("alertdialog")
        .getByRole("button", { name: t("app.platform.permissions.reset") })
        .click();
      await expect(page.getByText(t("app.platform.permissions.resetDone")).last()).toBeVisible();
      await expect(resetClient).toHaveCount(0);
    }

    // A previous run on the same database may have left the Clients customized.
    await expect(sessions).toBeEnabled();
    if (await resetClient.isVisible()) {
      await restoreClient();
    }

    await expect(sessions).not.toBeChecked();
    await sessions.click();
    await expect(page.getByText(t("app.platform.permissions.granted"))).toBeVisible();
    await expect(sessions).toBeChecked();
    await expect(customized).toBeVisible();

    await restoreClient();
    await expect(sessions).not.toBeChecked();
    await expect(customized).toHaveCount(0);
  });

  await test.step("specializations: one is created, given to a user, taken back and deleted", async () => {
    const name = `E2E ${Date.now().toString(36)}`;
    await page.goto(`/platform/tenants/${E2E.tenant}/specializations`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.specializations.title"), level: 1 }),
    ).toBeVisible();
    await expectAccessible(page, "specializations");

    await page.getByRole("button", { name: t("app.platform.specializations.new") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(t("app.platform.specializations.name")).fill(name);
    await dialog.getByLabel(t("Email")).fill("not-an-email");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(dialog.getByText(t("validation.specializations.email"))).toBeVisible();
    await dialog.getByLabel(t("Email")).fill("fisio@example.test");
    await dialog.getByLabel(t("app.platform.specializations.privateLabel")).click();
    await expectAccessible(page, "new specialization dialog");
    await dialog.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("app.platform.specializations.saved"))).toBeVisible();

    const table = page.getByRole("table", { name: t("app.platform.specializations.list") });
    const row = table.getByRole("row").filter({ hasText: name });
    await expect(row).toContainText(t("app.platform.specializations.private"));
    await row
      .getByRole("link", { name: t("app.platform.specializations.membersNamed", { name }) })
      .click();

    await expect(
      page.getByRole("heading", { name: t("app.platform.specializations.membersTitle"), level: 1 }),
    ).toBeVisible();
    await page.getByLabel(t("Search")).fill("mario");
    const candidate = page.getByRole("checkbox", { name: /Mario Rossi \(mario\.rossi\)/ });
    await candidate.click();
    await expectAccessible(page, "specialization members");
    await page
      .getByRole("button", { name: t("app.platform.specializations.addSelected", { count: 1 }) })
      .click();
    await expect(page.getByText(t("app.platform.specializations.added"))).toBeVisible();
    const members = page.getByRole("table", { name: t("app.platform.specializations.members") });
    await expect(members).toContainText("mario.rossi");

    await members
      .getByRole("button", {
        name: t("app.platform.specializations.removeNamed", { user: "Mario Rossi (mario.rossi)" }),
      })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.platform.specializations.remove") })
      .click();
    await expect(page.getByText(t("app.platform.specializations.removed"))).toBeVisible();
    await expect(members).not.toContainText("mario.rossi");

    await page.getByRole("link", { name: t("app.platform.specializations.back") }).click();
    await row
      .getByRole("button", { name: t("app.platform.specializations.deleteNamed", { name }) })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(page.getByText(t("app.platform.specializations.deleted"))).toBeVisible();
    await expect(row).toHaveCount(0);
  });

  await test.step("logs: debug level for a while, then the tenant's own events", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/logs`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.logs.title"), level: 1 }),
    ).toBeVisible();
    // Repeatable on a reused database: a run stopped half-way may have left Debug on.
    const disable = page.getByRole("button", { name: t("app.platform.logs.disableDebug") });
    await expect(
      page.getByRole("button", { name: t("app.platform.logs.enableDebug") }),
    ).toBeVisible();
    if (await disable.isVisible()) {
      await disable.click();
      await expect(disable).toHaveCount(0);
    }

    await expect(
      page.getByText(t("app.platform.logs.levelDefault", { level: "Information" })),
    ).toBeVisible();

    await page.getByRole("button", { name: t("app.platform.logs.enableDebug") }).click();
    await expect(page.getByText(t("app.platform.logs.debugEnabled"))).toBeVisible();
    await expect(disable).toBeVisible();

    // The change itself is a security event in the tenant's file (written in batches): search it by code.
    await page.getByLabel(t("app.platform.logs.code")).fill("AUX-29025");
    await expect(page).toHaveURL(/filter%5Bcode%5D=AUX-29025|filter\[code\]=AUX-29025/);
    const events = page.getByRole("list", { name: t("app.platform.logs.events") });
    await expect(async () => {
      await page.reload();
      await expect(events).toContainText("AUX-29025", { timeout: 1_000 });
    }).toPass({ timeout: 15_000 });

    // Newest first: the first event is the change just made (repeatable on a reused database).
    const newest = events.locator("details").first();
    await newest.locator("summary").click();
    await expect(newest.getByText(t("app.platform.logs.operation"), { exact: true })).toBeVisible();
    await expect(newest).toContainText("Tenancy.ChangeLogLevel");
    await expectAccessible(page, "tenant logs");

    await disable.click();
    await expect(page.getByText(t("app.platform.logs.debugDisabled"))).toBeVisible();
    await expect(disable).toHaveCount(0);
  });

  await test.step("imports: a type with its template, an upload waiting for the Worker, its details", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/imports`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.imports.title"), level: 1 }),
    ).toBeVisible();
    const typeName = `Servizi E2E ${Date.now().toString(36)}`;
    await page.getByRole("combobox", { name: t("app.platform.imports.entity") }).click();
    await page.getByRole("option", { name: t("app.platform.imports.entities.Service") }).click();
    await page.getByLabel(t("app.platform.imports.typeName")).fill(typeName);
    await page.getByRole("button", { name: t("app.platform.imports.newType") }).click();
    await expect(page.getByText(t("app.platform.imports.typeCreated"))).toBeVisible();

    const types = page.getByRole("table", { name: t("app.platform.imports.typesTitle") });
    await expect(types.getByText(typeName)).toBeVisible();
    await page
      .getByRole("button", { name: t("app.platform.imports.fieldsNamed", { type: typeName }) })
      .click();
    const fields = page.getByRole("dialog");
    await expect(fields.getByText("durationDays")).toBeVisible();
    await expectAccessible(page, "import columns");
    await page.keyboard.press("Escape");

    const downloading = page.waitForEvent("download");
    await page
      .getByRole("button", { name: t("app.platform.imports.templateNamed", { type: typeName }) })
      .click();
    const template = await downloading;
    expect(template.suggestedFilename()).toMatch(/_template\.xlsx$/);
    // The download is saved under a random name: upload it with its own (.xlsx).
    const file = {
      name: template.suggestedFilename(),
      mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      buffer: await readFile(await template.path()),
    };

    // No Worker in the E2E run: the upload waits for validation.
    const importName = `Import E2E ${Date.now().toString(36)}`;
    await page.getByLabel(t("app.platform.imports.name")).fill(importName);
    await page.getByRole("combobox", { name: t("app.platform.imports.type") }).click();
    await page.getByRole("option", { name: typeName }).click();
    await page.getByLabel(t("app.platform.imports.file")).setInputFiles(file);
    await page.getByRole("button", { name: t("app.platform.imports.upload") }).click();
    await expect(page.getByText(t("app.platform.imports.started"))).toBeVisible();
    const jobs = page.getByRole("table", { name: t("app.platform.imports.listTitle") });
    await expect(jobs.getByRole("link", { name: importName, exact: true })).toBeVisible();
    await expect(jobs.getByText(t("app.platform.imports.status.Pending")).first()).toBeVisible();
    await expectAccessible(page, "imports");

    await jobs.getByRole("link", { name: importName, exact: true }).click();
    await expect(page).toHaveURL(/\/imports\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { name: importName })).toBeVisible();
    await expect(page.getByRole("progressbar")).toBeVisible();
    await expectAccessible(page, "import details");
  });

  await test.step("jobs: the registered jobs with their last run, and a run queued for the Worker", async () => {
    await page.goto(`/platform/tenants/${E2E.tenant}/jobs`);
    await expect(
      page.getByRole("heading", { name: t("app.platform.jobs.title"), level: 1 }),
    ).toBeVisible();
    const expiry = t("app.platform.jobs.catalog.casesExpiry.name");
    await expect(page.getByRole("cell", { name: new RegExp(expiry) }).first()).toBeVisible();
    await expectAccessible(page, "jobs");

    // The suite runs no Worker: the request is queued, which is what the page promises.
    await page
      .getByRole("button", { name: t("app.platform.jobs.runNamed", { name: expiry }) })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.platform.jobs.run") })
      .click();
    await expect(page.getByText(t("app.platform.jobs.requested"))).toBeVisible();
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
