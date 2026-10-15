import { expect, test } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Connected accounts in the profile (N05, C-03): an Employee opens the tab, finds only "Connect PEC / IMAP" (the E2E API
 * has no Google / Microsoft application), gets the server filled in from a PEC domain and a port refused by the API
 * before any connection, and comes back from a consent the API cannot complete with its error code. Real accounts and
 * an IMAP test server come with C-08.
 */
test("an Employee opens the connected accounts of the profile", async ({ page }) => {
  await signInWithNewPassword(page, E2E.accountsUser.userName);

  await test.step("the tab lists no account and offers PEC / IMAP only", async () => {
    await page.goto(`/${E2E.tenant}/profile?tab=accounts`);
    await expect(page.getByRole("tab", { name: t("app.profile.tabs.accounts") })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await expect(
      page.getByRole("heading", { name: t("app.connectedAccounts.title") }),
    ).toBeVisible();
    await expect(page.getByText(t("app.connectedAccounts.empty"))).toBeVisible();
    await expect(page.getByText(t("app.connectedAccounts.oauthNotConfigured"))).toBeVisible();
    await expect(
      page.getByRole("button", { name: t("app.connectedAccounts.signInWith.google") }),
    ).toHaveCount(0);
    await expectAccessible(page, "profile-connected-accounts");
    await expectNoHorizontalScroll(page);
  });

  await test.step("PEC / IMAP: server from the domain, port checked by the API", async () => {
    await page.getByRole("button", { name: t("app.connectedAccounts.imap") }).click();
    const dialog = page.getByRole("dialog", { name: t("app.connectedAccounts.imap") });
    await dialog.getByLabel(t("app.connectedAccounts.address")).fill("studio.e2e@pec.it");
    await dialog.getByLabel(t("app.connectedAccounts.address")).blur();
    await expect(dialog.getByLabel(t("app.connectedAccounts.host"))).toHaveValue(
      "imaps.pec.aruba.it",
    );
    await expect(dialog.getByLabel(t("app.connectedAccounts.userName"))).toHaveValue(
      "studio.e2e@pec.it",
    );
    await expect(dialog.getByText(t("app.connectedAccounts.serverKnown"))).toBeVisible();
    await expectAccessible(page, "profile-connected-accounts-imap");

    await dialog.getByRole("textbox", { name: `${t("Password")} *` }).fill("not-a-real-password");
    await dialog.getByLabel(t("app.connectedAccounts.port")).fill("2525");
    await dialog.getByRole("button", { name: t("app.connectedAccounts.testAndConnect") }).click();
    await expect(dialog.getByText(t("validation.mailbox.portNotAllowed"))).toBeVisible();
    await dialog.getByRole("button", { name: t("Cancel") }).click();
    await expect(page.getByText(t("app.connectedAccounts.empty"))).toBeVisible();
  });

  await test.step("a consent the API cannot complete comes back with its code", async () => {
    await page.goto("/api/connected-accounts/callback/google?code=e2e&state=e2e");
    await expect(page).toHaveURL(`/${E2E.tenant}/profile?tab=accounts`);
    const alert = page.getByRole("alert").filter({ hasText: t("errors.AUX-30007") });
    await expect(alert).toBeVisible();
    await expect(alert.getByTestId("error-code")).toHaveText("AUX-30007");
  });
});
