import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Requests, notifications and sessions (B-21, F15–F17): a client asks her operator, the operator is notified (bell),
 * opens the request from the notification and replies; the client reads the reply and closes the request; an
 * Administrator sees the operator's session and ends it, and the operator's tab signs out by itself (ForceLogout).
 */

const openRequests = async (page: Page) => {
  await page
    .getByRole("navigation", { name: t("app.shell.navigation") })
    .getByRole("link", { name: t("nav.requests") })
    .click();
  await expect(page.getByRole("heading", { name: t("nav.requests"), level: 1 })).toBeVisible();
};

test("a request goes from the client to her operator and back, then the operator's session ends", async ({
  page,
  browser,
}) => {
  const employee = E2E.requestsEmployee;
  const client = E2E.requestsClient;
  const subject = `Fattura ${Date.now().toString(36)}`;

  await test.step("the client sends a request to her operator", async () => {
    await signInWithNewPassword(page, client.userName);
    await openRequests(page);
    await expectAccessible(page, "client requests");
    await page.getByRole("button", { name: t("NewRequest") }).click();
    const dialog = page.getByRole("dialog", { name: t("NewRequest") });
    await expect(dialog.getByLabel(t("app.requests.askMyOperator"))).toBeChecked();
    await dialog.getByLabel(`${t("Subject")} *`).fill(subject);
    await dialog.getByLabel(`${t("Message")} *`).fill("Non trovo la fattura di settembre.");
    await expectAccessible(page, "new request");
    await dialog.getByRole("button", { name: t("Send") }).click();
    await expect(toast(page, t("app.requests.sent"))).toBeVisible();
    await expect(page.getByRole("heading", { name: subject, level: 1 })).toBeVisible();
    await expect(page.getByText(`${client.fullName} → ${employee.fullName}`)).toBeVisible();
    await expectAccessible(page, "request thread");
  });

  const employeePage = await (await browser.newContext()).newPage();

  await test.step("the operator is notified and replies from the notification", async () => {
    await signInWithNewPassword(employeePage, employee.userName);
    const bell = employeePage.getByRole("button", {
      name: t("app.notifications.unreadCount", { count: 1 }),
    });
    await expect(bell).toBeVisible();
    await bell.click();
    const panel = employeePage.getByRole("list", { name: t("Notifications") });
    await expect(panel).toContainText(subject);
    await expectAccessible(employeePage, "notification panel");
    await panel.getByRole("button").filter({ hasText: subject }).click();
    await expect(employeePage.getByRole("heading", { name: subject, level: 1 })).toBeVisible();
    await employeePage.getByLabel(t("app.requests.reply")).fill("Te l'ho inviata ieri per e-mail.");
    await employeePage.getByRole("button", { name: t("Send") }).click();
    await expect(toast(employeePage, t("app.requests.replied"))).toBeVisible();
    await expect(
      employeePage.getByRole("list", { name: t("app.requests.thread") }).getByRole("listitem"),
    ).toHaveCount(2);
  });

  await test.step("the client reads the reply and closes the request", async () => {
    await page.reload();
    await expect(page.getByText(t("app.requests.status.Responded"))).toBeVisible();
    await expect(page.getByText("Te l'ho inviata ieri per e-mail.")).toBeVisible();
    await page.getByRole("button", { name: t("app.requests.close") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.requests.close") })
      .click();
    await expect(toast(page, t("app.requests.closed"))).toBeVisible();
    await expect(page.getByText(t("app.requests.isClosed"))).toBeVisible();
    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step("the client's notification page has the preferences", async () => {
    await page.goto(`/${E2E.tenant}/notifications`);
    await expect(page.getByRole("heading", { name: t("Notifications"), level: 1 })).toBeVisible();
    const title = t("notifications.request.replied.title");
    await page.getByRole("switch", { name: `${title}: ${t("Email")}` }).click();
    await expect(toast(page, t("app.notifications.preferencesSaved"))).toBeVisible();
    await expect(page.getByRole("switch", { name: `${title}: ${t("Email")}` })).toBeChecked();
    await expectAccessible(page, "notifications page");
  });

  await test.step("an Administrator ends the operator's session and her tab signs out", async () => {
    await page.context().clearCookies();
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.sessions") })
      .click();
    await expect(page.getByRole("heading", { name: t("ActiveSessions"), level: 1 })).toBeVisible();
    await expect(page.getByText(t("app.sessions.cards.sessions"))).toBeVisible();
    await page.locator("#sessions-user").fill(employee.userName);
    const revoke = page.getByRole("button", {
      name: t("app.sessions.revokeOf", { name: employee.fullName }),
    });
    await expect(revoke).toBeVisible();
    await expectAccessible(page, "sessions");
    await revoke.click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.sessions.revoke") })
      .click();
    await expect(toast(page, t("app.sessions.revoked"))).toBeVisible();

    // ForceLogout over the realtime hub: no reload needed.
    await expect(employeePage).toHaveURL(new RegExp(`/${E2E.tenant}/login`), { timeout: 15_000 });
    await employeePage.context().close();
  });
});
