import { expect, test, type Page } from "@playwright/test";

import { auxctlSecret } from "./support/auxctl";
import { E2E } from "./support/env";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Client pages (B-04, F05): an Administrator creates a client with the wizard and works on its 360° view (data,
 * employee, sign-in rule Q60, temporary password, delete); an Employee creates a client for themselves and finds it
 * among "my clients". The users have their own passwords, reset here, so the file runs in any order.
 */

/** A valid fiscal code nobody else has (random letters and digits in the right places). */
function newFiscalCode(): string {
  const letter = () => String.fromCharCode(65 + Math.floor(Math.random() * 26));
  const digit = () => String(Math.floor(Math.random() * 10));
  return [
    letter(),
    letter(),
    letter(),
    letter(),
    letter(),
    letter(),
    digit(),
    digit(),
    "A",
    digit(),
    digit(),
    letter(),
    digit(),
    digit(),
    digit(),
    letter(),
  ].join("");
}

/** Signs in with a temporary password set by auxctl, replacing it with a new one, and opens the clients. */
async function signIn(page: Page, userName: string) {
  const temporary = auxctlSecret(
    "users",
    "reset-password",
    "--tenant",
    E2E.tenant,
    "--user",
    userName,
  );
  const password = `E2e-Pw-${Date.now()}!`;
  await page.goto(`/${E2E.tenant}/login`);
  await page.getByLabel(t("Username")).fill(userName);
  await page.getByLabel(t("Password"), { exact: true }).fill(temporary);
  await page.getByRole("button", { name: t("Login") }).click();
  await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/password-expired`));
  await page.getByLabel(t("CurrentPassword")).fill(temporary);
  await page.getByLabel(t("NewPassword"), { exact: true }).fill(password);
  await page.getByLabel(t("ConfirmNewPassword")).fill(password);
  await page.getByRole("button", { name: t("ChangePassword") }).click();
  await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);
  await page
    .getByRole("navigation", { name: t("app.shell.navigation") })
    .getByRole("link", { name: t("nav.clients") })
    .click();
  await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/clients`));
}

/** A toast with the text (sonner), not the same words elsewhere on the page. */
function toast(page: Page, text: string) {
  return page.locator("[data-sonner-toast]").filter({ hasText: text });
}

/** Fills the first step of the wizard and goes on. */
async function personalData(page: Page, lastName: string) {
  await page.getByRole("link", { name: t("AddClient") }).click();
  await expect(page).toHaveURL(`/${E2E.tenant}/clients/new`);
  await page.getByLabel(`${t("Name")} *`, { exact: true }).fill("Giulia");
  await page.getByLabel(`${t("Surname")} *`, { exact: true }).fill(lastName);
  await page.getByLabel(`${t("BirthDate")} *`).fill("1985-05-20");
  await page.getByLabel(`${t("app.clients.fiscalCode")} *`).fill(newFiscalCode());
  await page.getByRole("button", { name: t("app.clients.wizard.next") }).click();
}

test("an Administrator creates a client and works on its 360° view", async ({ page }) => {
  const lastName = `Verdi${Date.now()}`;
  const email = `giulia.${Date.now()}@example.test`;

  await test.step("the list is accessible", async () => {
    await signIn(page, E2E.administrator.userName);
    await expect(page.getByRole("heading", { name: t("Clients"), level: 1 })).toBeVisible();
    await expectAccessible(page, "clients");
  });

  await test.step("the wizard checks each step and creates the client for an employee", async () => {
    await page.getByRole("link", { name: t("AddClient") }).click();
    await page.getByRole("button", { name: t("app.clients.wizard.next") }).click();
    await expect(page.getByText(t("validation.person.fiscalCode"))).toBeVisible();
    await expectAccessible(page, "new client wizard");
    await page.goto(`/${E2E.tenant}/clients`);

    await personalData(page, lastName);
    await page.getByLabel(`${t("Email")} *`).fill(email);
    await page.getByLabel(t("Phone")).fill("333 1234567");
    await page.getByRole("combobox", { name: t("app.clients.employee") }).click();
    await page.getByRole("option", { name: E2E.employee.fullName }).click();
    await page.getByRole("button", { name: t("app.clients.wizard.next") }).click();
    await expect(
      page.getByRole("definition").filter({ hasText: E2E.employee.fullName }),
    ).toBeVisible();
    await page.getByRole("button", { name: t("app.clients.wizard.create") }).click();
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/clients/[0-9a-f-]{36}`));
    await expect(page.getByRole("heading", { name: `Giulia ${lastName}`, level: 1 })).toBeVisible();
    await expectAccessible(page, "client overview");
  });

  await test.step("personal data are edited in place", async () => {
    await page.getByRole("tab", { name: t("app.clients.tabs.data") }).click();
    await page.getByLabel(t("Phone")).fill("06 1234 5678");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.clients.saved"))).toBeVisible();
    await expectAccessible(page, "client data");
  });

  await test.step("without an employee the sign-in cannot be enabled (Q60)", async () => {
    await page.getByRole("tab", { name: t("app.clients.tabs.assignment") }).click();
    await expect(page.getByRole("table", { name: t("app.clients.history") })).toContainText(
      E2E.employee.fullName,
    );
    await page.getByRole("button", { name: t("app.clients.unassign") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.clients.unassign") })
      .click();
    await expect(toast(page, t("app.clients.unassigned"))).toBeVisible();
    await expectAccessible(page, "client employee");

    await page.getByRole("tab", { name: t("app.clients.tabs.access") }).click();
    const signIn = page.getByRole("switch", { name: t("app.clients.canSignIn") });
    await signIn.click();
    await expect(toast(page, t("app.clients.signInDisabledToast"))).toBeVisible();
    await signIn.click();
    await expect(toast(page, t("errors.AUX-13019"))).toBeVisible();
    await expectAccessible(page, "client account");
  });

  await test.step("a temporary password is shown once", async () => {
    await page.getByRole("button", { name: t("app.clients.resetTemporary") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("ResetPassword") })
      .click();
    await expect(page.getByText(t("app.clients.temporaryPassword"), { exact: true })).toBeVisible();
  });

  await test.step("the client is deleted and leaves the list", async () => {
    await page.getByRole("button", { name: t("Delete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/clients(\\?.*)?$`));
    await page.getByLabel(t("Email"), { exact: true }).fill(email);
    await expect(page.getByRole("table", { name: t("Clients") })).not.toContainText(lastName);
  });
});

test("an Employee creates a client for themselves and finds it among their clients", async ({
  page,
}) => {
  const lastName = `Gialli${Date.now()}`;
  await signIn(page, E2E.employee.userName);
  await expect(page.getByRole("button", { name: t("MyClients"), pressed: true })).toBeVisible();

  await personalData(page, lastName);
  await page.getByLabel(`${t("Email")} *`).fill(`gialli.${Date.now()}@example.test`);
  await expect(page.getByRole("combobox", { name: t("app.clients.employee") })).toHaveCount(0);
  await page.getByRole("button", { name: t("app.clients.wizard.next") }).click();
  await expect(page.getByText(t("app.clients.wizard.assignedToYou"))).toBeVisible();
  await page.getByRole("button", { name: t("app.clients.wizard.create") }).click();
  await expect(page.getByRole("heading", { name: `Giulia ${lastName}`, level: 1 })).toBeVisible();
  await expect(page.getByRole("button", { name: t("Delete") })).toHaveCount(0);

  await page.getByRole("link", { name: t("app.clients.back") }).click();
  await expect(page.getByRole("table", { name: t("Clients") })).toContainText(lastName);

  await page.setViewportSize({ width: 360, height: 780 });
  await expectNoHorizontalScroll(page);
  await expectAccessible(page, "my clients (phone)");
});
