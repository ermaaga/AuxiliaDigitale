import { expect, test, type Page } from "@playwright/test";

import { auxctl, auxctlSecret } from "./support/auxctl";
import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { nextTotpStep, totp } from "./support/totp";
import { expectAccessible, t } from "./support/ui";

const user = E2E.twoFactorUser;
const login = `/${E2E.tenant}/login`;

/**
 * N04: the authenticator app offered after the temporary password, enrolled with the QR code, asked at every sign-in
 * (wrong code refused), "stay signed in" with a persistent cookie, reset by an Administrator ending the session. The
 * spec owns its users and resets the app first with auxctl, so it runs again on the same database.
 */
test("authenticator app and stay signed in", async ({ page, browser }) => {
  test.setTimeout(120_000);
  auxctl("users", "reset-mfa", "--tenant", E2E.tenant, "--user", user.userName);
  const temporary = auxctlSecret(
    "users",
    "reset-password",
    "--tenant",
    E2E.tenant,
    "--user",
    user.userName,
  );
  const password = `E2e-Pw-${Date.now()}!`;
  let secret = "";

  await test.step("after the temporary password the app is offered and enrolled with the QR code", async () => {
    await signIn(page, temporary);
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/password-expired`));
    await page.getByLabel(t("CurrentPassword")).fill(temporary);
    await page.getByLabel(t("NewPassword"), { exact: true }).fill(password);
    await page.getByLabel(t("ConfirmNewPassword")).fill(password);
    await page.getByRole("button", { name: t("ChangePassword") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/profile?twoFactor=suggest`);

    await page.getByRole("button", { name: t("app.twoFactor.setUp") }).click();
    await expect(page.getByRole("img", { name: t("app.platform.activate.qrLabel") })).toBeVisible();
    await expectAccessible(page, "authenticator enrolment");
    secret = (await page.getByTestId("totp-secret").innerText()).replace(/\s/g, "");
    await page.getByLabel(t("app.twoFactor.codeLabel")).fill(totp(secret));
    await page.getByRole("button", { name: t("app.twoFactor.activate"), exact: true }).click();
    await expect(toast(page, t("app.twoFactor.enabledToast"))).toBeVisible();
    await expect(page.getByText(t("app.twoFactor.active"), { exact: true })).toBeVisible();
  });

  await test.step("the sign-in asks for the code and refuses a wrong one", async () => {
    await page.context().clearCookies();
    await signIn(page, password);
    const code = page.getByLabel(t("app.twoFactor.codeLabel"));
    await expect(code).toBeVisible();
    await expectAccessible(page, "authenticator code");

    const current = totp(secret);
    await code.fill(current === "000000" ? "111111" : "000000");
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page.getByTestId("error-code")).toHaveText("AUX-12073");
  });

  await test.step("the right code with 'stay signed in' opens a persistent session", async () => {
    // The enrolment used the current step: a code of the next one.
    await nextTotpStep();
    await page.getByRole("button", { name: t("app.twoFactor.back") }).click();
    await page.getByLabel(t("app.auth.staySignedIn", { days: 14 })).check();
    await page.getByRole("button", { name: t("Login") }).click();
    await page.getByLabel(t("app.twoFactor.codeLabel")).fill(totp(secret));
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);

    const cookie = (await page.context().cookies()).find((item) => item.name === "__Host-aux_sid");
    expect(cookie?.expires ?? -1).toBeGreaterThan(Date.now() / 1000 + 13 * 86_400);
  });

  await test.step("an Administrator resets the app: the user's session ends", async () => {
    const admin = await browser.newContext();
    const adminPage = await admin.newPage();
    await signInWithNewPassword(adminPage, E2E.twoFactorAdministrator.userName);
    await adminPage.goto(`/${E2E.tenant}/employees/${user.id}?tab=access`);
    await adminPage.getByRole("button", { name: t("app.twoFactor.reset") }).click();
    await adminPage
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.twoFactor.reset") })
      .click();
    await expect(toast(adminPage, t("app.twoFactor.resetDone"))).toBeVisible();
    await admin.close();

    await page.goto(`/${E2E.tenant}/profile`);
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/login`));
    await signIn(page, password);
    await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);
  });
});

async function signIn(page: Page, password: string) {
  if (!page.url().includes("/login")) {
    await page.goto(login);
  }

  await page.getByLabel(t("Username")).fill(user.userName);
  await page.getByLabel(t("Password"), { exact: true }).fill(password);
  await page.getByRole("button", { name: t("Login") }).click();
}
