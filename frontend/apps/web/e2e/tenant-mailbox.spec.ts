import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Mail and connected calendars with copied data (N05, C-08): an Employee with a PEC mailbox and a Microsoft calendar
 * (e2e/support/mailbox-seed.sql, seeded again before every run in place of the providers) finds the headers per folder
 * at once while the sync is asked, searches and filters them, sees today's event of the calendar in the appointments
 * calendar in the account's colour, read-only, and hides it from the legend; changes a colour and disconnects the
 * mailbox, whose copied messages disappear. Reading live, .eml, filing and the sync itself are covered by the backend
 * tests (fake Gmail, GreenMail IMAP server); another user never sees these accounts (MailboxIsolationTests, and the
 * empty profile of tenant-connected-accounts.spec.ts).
 */
const pec = "studio.rinaldi@pec.it";
const outlook = "Silvia (Outlook)";
const event = "Riunione con il commercialista";

const folders = (page: Page) => page.getByRole("navigation", { name: t("app.mail.folders") });
const messages = (page: Page) => page.getByRole("region", { name: t("app.mail.messages") });
const rows = (page: Page) => messages(page).getByRole("listitem");

test("an Employee works with the copied mail and calendar of her connected accounts", async ({
  page,
}) => {
  await signInWithNewPassword(page, E2E.mailboxUser.userName);

  await test.step("the mail page shows the stored headers at once and asks for a sync", async () => {
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.mail"), exact: true })
      .click();
    await expect(page.getByRole("heading", { level: 1, name: t("nav.mail") })).toBeVisible();
    await expect(page.getByRole("status").filter({ hasText: t("app.mail.syncing") })).toBeVisible();

    await expect(folders(page).getByText(pec)).toBeVisible();
    await expect(folders(page).getByRole("button", { name: t("app.mail.inbox") })).toContainText(
      "1",
    );
    await expect(rows(page)).toHaveCount(4);
    const newest = rows(page).first();
    await expect(newest).toContainText("Mario Rossi");
    await expect(newest).toContainText("Fattura di ottobre");
    await expect(newest.getByLabel(t("app.mail.hasAttachments"))).toBeVisible();
    await expect(newest).toContainText(t("app.mail.unread"));
    await expectAccessible(page, "mail-list");
    await expectNoHorizontalScroll(page);
  });

  await test.step("folders, search and the unread filter work on the stored headers", async () => {
    await folders(page)
      .getByRole("button", { name: t("app.mail.sent") })
      .click();
    await expect(rows(page)).toHaveCount(1);
    await expect(rows(page).first()).toContainText(`${t("app.mail.to")} Mario Rossi`);

    await folders(page)
      .getByRole("button", { name: t("app.mail.allMail") })
      .click();
    await messages(page)
      .getByRole("searchbox", { name: t("app.mail.search") })
      .fill("CONSEGNA");
    await expect(rows(page)).toHaveCount(1);
    await expect(rows(page).first()).toContainText("Dichiarazione dei redditi");
    await messages(page)
      .getByRole("searchbox", { name: t("app.mail.search") })
      .fill("");
    await messages(page).getByLabel(t("app.mail.unreadOnly")).click();
    await expect(rows(page)).toHaveCount(1);
    await expect(rows(page).first()).toContainText("Fattura di ottobre");
  });

  await test.step("today's event of the connected calendar is in the appointments calendar", async () => {
    await page.goto(`/${E2E.tenant}/appointments?view=calendar`);
    const legend = page.getByRole("region", { name: t("app.externalCalendar.legend") });
    const account = legend.getByRole("button", { name: outlook });
    await expect(account).toHaveAttribute("aria-pressed", "true");
    const item = page.locator(".fc-event").filter({ hasText: event });
    await expect(item).toBeVisible();
    await expectAccessible(page, "appointments-with-connected-calendar");

    await item.click();
    const details = page.getByRole("dialog", { name: event });
    await expect(details.getByText(t("app.externalCalendar.readOnly"))).toBeVisible();
    await expect(details.getByText("Via Roma 1, Milano")).toBeVisible();
    await expect(
      details.getByRole("link", {
        name: t("app.externalCalendar.openAt", {
          provider: t("app.connectedAccounts.providers.microsoft"),
        }),
      }),
    ).toHaveAttribute("href", "https://outlook.live.com/calendar/item/e2e-event-1");
    await expectAccessible(page, "connected-calendar-event");
    await page.keyboard.press("Escape");

    await account.click();
    await expect(account).toHaveAttribute("aria-pressed", "false");
    await expect(item).toHaveCount(0);
    await account.click();
    await expect(item).toBeVisible();
  });

  await test.step("the profile changes a colour and disconnects the mailbox with its copied mail", async () => {
    await page.goto(`/${E2E.tenant}/profile?tab=accounts`);
    const rows = page.getByTestId("connected-account");
    await expect(rows).toHaveCount(2);
    const mailbox = rows.filter({ hasText: pec });
    await expect(mailbox).toContainText(
      t("app.connectedAccounts.colorName", { color: t("app.connectedAccounts.colors.teal") }),
    );

    await mailbox
      .getByRole("button", { name: t("app.connectedAccounts.actionsFor", { address: pec }) })
      .click();
    await page.getByRole("menuitem", { name: t("app.connectedAccounts.color") }).click();
    await page
      .getByRole("menuitemradio", { name: t("app.connectedAccounts.colors.orange") })
      .click();
    await expect(toast(page, t("app.connectedAccounts.colorSaved"))).toBeVisible();
    await expect(mailbox).toContainText(
      t("app.connectedAccounts.colorName", { color: t("app.connectedAccounts.colors.orange") }),
    );

    await mailbox
      .getByRole("button", { name: t("app.connectedAccounts.actionsFor", { address: pec }) })
      .click();
    await page.getByRole("menuitem", { name: t("app.connectedAccounts.disconnect") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.connectedAccounts.disconnect") })
      .click();
    await expect(toast(page, t("app.connectedAccounts.disconnected"))).toBeVisible();
    await expect(rows).toHaveCount(1);

    // The calendar-only account has no mail: the mail page offers to connect a mailbox.
    await page.goto(`/${E2E.tenant}/mail`);
    await expect(page.getByText(t("app.mail.noAccounts"))).toBeVisible();
  });
});
