import { expect, type Page } from "@playwright/test";

import { auxctlSecret } from "./auxctl";
import { E2E } from "./env";
import { t } from "./ui";

/**
 * Signs a seeded tenant user in with a temporary password set by auxctl, replacing it with a new one (returned), and
 * waits for the dashboard. Every spec owns its users, so the files run in any order and again on the same database.
 */
export async function signInWithNewPassword(page: Page, userName: string): Promise<string> {
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
  return password;
}

/** A toast with the text (sonner), not the same words elsewhere on the page. */
export function toast(page: Page, text: string) {
  return page.locator("[data-sonner-toast]").filter({ hasText: text });
}
