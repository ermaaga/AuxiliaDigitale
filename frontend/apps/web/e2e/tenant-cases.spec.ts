import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Case pages (B-14, F09, F33): an Administrator opens a case for a client, moves it forward, files a document in a
 * folder of the service, completes it with the amount received and finds it in the client's 360° tab.
 */

/** The status stepper of the case detail (the timeline below repeats the status names). */
const stepper = (page: Page) => page.getByRole("list", { name: t("app.cases.progress") });

const pdf = {
  name: "redditi.pdf",
  mimeType: "application/pdf",
  buffer: Buffer.from("%PDF-1.4\n% e2e case\n"),
};

test("an Administrator runs a case from opening to completion", async ({ page }) => {
  const client = E2E.casesClient;
  const service = E2E.caseService;

  await test.step("the case list is accessible", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.cases") })
      .click();
    await expect(page.getByRole("heading", { name: t("nav.cases"), level: 1 })).toBeVisible();
    await expectAccessible(page, "cases");
  });

  await test.step("a case is opened from the quick creation", async () => {
    await page.getByRole("button", { name: t("AddSubscription") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("combobox", { name: `${t("Client")} *` }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill("Ferri");
    await page.getByRole("option", { name: /Ferri Marco/ }).click();
    await dialog.getByRole("combobox", { name: `${t("Membership")} *` }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill("E2E");
    await page.getByRole("option", { name: new RegExp(service.name) }).click();
    await expectAccessible(page, "new case");
    await dialog.getByRole("button", { name: t("Create") }).click();
    await expect(toast(page, t("app.cases.opened"))).toBeVisible();
    await expect(
      page.getByRole("heading", { name: `${service.name} — ${client.fullName}`, level: 1 }),
    ).toBeVisible();
    await expect(
      stepper(page)
        .getByRole("listitem")
        .filter({ hasText: t("Inserted") }),
    ).toHaveAttribute("aria-current", "step");
    await expectAccessible(page, "case inserted");
  });

  await test.step("in progress, a document goes into a folder of the service", async () => {
    await page.getByRole("button", { name: t("app.cases.forward") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.cases.forward") })
      .click();
    await expect(toast(page, t("app.cases.advanced"))).toBeVisible();
    await page
      .getByRole("list", { name: t("app.cases.folders") })
      .getByRole("button", { name: service.folder, exact: true })
      .click();
    await page.getByRole("button", { name: t("UploadDocument") }).click();
    await page.getByLabel(t("SelectFiles"), { exact: true }).setInputFiles(pdf);
    await page.getByRole("button", { name: t("Upload"), exact: true }).click();
    await expect(toast(page, t("app.documents.uploadedOne"))).toBeVisible();
    await expect(page.getByRole("table", { name: service.folder })).toContainText(pdf.name);
    await expectAccessible(page, "case documents");
  });

  await test.step("sent, it is completed with the amount received", async () => {
    await page.getByRole("button", { name: t("app.cases.forward") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.cases.forward") })
      .click();
    await expect(
      stepper(page)
        .getByRole("listitem")
        .filter({ hasText: t("Sent") }),
    ).toHaveAttribute("aria-current", "step");
    await page.getByRole("button", { name: t("app.cases.complete") }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.getByLabel(`${t("app.cases.amountReceived")} *`)).toHaveValue("150.00");
    await expectAccessible(page, "complete case");
    await dialog.getByRole("button", { name: t("app.cases.complete") }).click();
    await expect(toast(page, t("SubscriptionCompletedSuccess"))).toBeVisible();
    await expect(page.getByRole("table", { name: t("app.cases.payments") })).toContainText("150");
    await expect(page.getByRole("button", { name: t("app.cases.forward") })).toHaveCount(0);
  });

  await test.step("the client's 360° tab lists the case", async () => {
    await page.goto(`/${E2E.tenant}/clients/${client.id}?tab=cases`);
    await expect(page.getByRole("table", { name: t("nav.cases") })).toContainText(service.name);
    await expectAccessible(page, "client cases");
    await expectNoHorizontalScroll(page);
  });
});
