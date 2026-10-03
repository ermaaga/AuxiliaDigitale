import { expect, test } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Document pages (B-13, F14): an Administrator uploads a file for a client from the documents page, finds it in the
 * client's 360° tab, opens the detail drawer, renames it (the extension stays), downloads it and deletes it.
 */

const pdf = {
  name: "Contratto firmato.pdf",
  mimeType: "application/pdf",
  buffer: Buffer.from("%PDF-1.4\n% e2e\n"),
};

test("an Administrator uploads, renames, downloads and deletes a document", async ({ page }) => {
  const client = E2E.documentsClient;

  await test.step("the documents page is accessible", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.documents") })
      .click();
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/documents`));
    await expect(
      page.getByRole("heading", { name: t("app.documents.title"), level: 1 }),
    ).toBeVisible();
    await expectAccessible(page, "documents");
  });

  await test.step("a file is uploaded for the client", async () => {
    await page.getByRole("button", { name: t("UploadDocument") }).click();
    await page.getByLabel(t("SelectFiles"), { exact: true }).setInputFiles(pdf);
    await expect(page.getByRole("list", { name: t("app.documents.selected") })).toContainText(
      pdf.name,
    );
    await page.getByRole("combobox", { name: `${t("Client")} *` }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill("Russo");
    await page.getByRole("option", { name: /Russo Elena/ }).click();
    await expectAccessible(page, "document uploader");
    await page.getByRole("button", { name: t("Upload"), exact: true }).click();
    await expect(toast(page, t("app.documents.uploadedOne"))).toBeVisible();
  });

  await test.step("the client's 360° tab lists it", async () => {
    await page.goto(`/${E2E.tenant}/clients/${client.id}?tab=documents`);
    const table = page.getByRole("table", { name: t("app.documents.title") });
    await expect(table).toContainText("Contratto_firmato.pdf");
    await expectAccessible(page, "client documents");
    await expectNoHorizontalScroll(page);
  });

  await test.step("the drawer renames it, keeping the extension, and downloads it", async () => {
    await page.getByRole("button", { name: "Contratto_firmato.pdf" }).click();
    const drawer = page.getByRole("dialog");
    await expect(
      drawer.getByText(t("app.documents.extensionKept", { extension: ".pdf" })),
    ).toBeVisible();
    await drawer.getByLabel(`${t("FileName")} *`).fill("Contratto 2026");
    await drawer.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.documents.saved"))).toBeVisible();
    await expect(drawer.getByRole("heading", { name: "Contratto_2026.pdf" })).toBeVisible();
    await expectAccessible(page, "document drawer");

    const download = page.waitForEvent("download");
    await drawer.getByRole("link", { name: t("Download") }).click();
    expect((await download).suggestedFilename()).toBe("Contratto_2026.pdf");
  });

  await test.step("the document is deleted", async () => {
    const drawer = page.getByRole("dialog");
    await drawer.getByRole("button", { name: t("Delete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(toast(page, t("app.documents.deleted"))).toBeVisible();
    await expect(page.getByRole("table", { name: t("app.documents.title") })).not.toContainText(
      "Contratto_2026.pdf",
    );
  });
});
