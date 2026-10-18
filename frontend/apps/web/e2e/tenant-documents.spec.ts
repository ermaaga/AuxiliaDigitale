import { expect, test } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Document pages (B-13, F14): an Administrator uploads a file for a client from the documents page, finds it in the
 * client's 360° tab, opens the detail drawer, previews it (PDF.js page, text file; ADR 0020), renames it (the extension
 * stays), downloads it and deletes it.
 */

/** A one-page PDF with real cross-reference offsets, so that PDF.js can draw it. */
function onePagePdf(text: string): Buffer {
  const stream = `BT /F1 24 Tf 40 100 Td (${text}) Tj ET`;
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
  ];
  let body = "%PDF-1.4\n";
  const offsets = objects.map((object, index) => {
    const offset = body.length;
    body += `${index + 1} 0 obj\n${object}\nendobj\n`;
    return offset;
  });
  const xref = body.length;
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  body += offsets.map((offset) => `${String(offset).padStart(10, "0")} 00000 n \n`).join("");
  body += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(body, "latin1");
}

const pdf = {
  name: "Contratto firmato.pdf",
  mimeType: "application/pdf",
  buffer: onePagePdf("Contratto E2E"),
};

const csv = {
  name: "Elenco.csv",
  mimeType: "text/csv",
  buffer: Buffer.from("nome;città\nRossi;Forlì\n", "utf8"),
};

test("an Administrator uploads, previews, renames, downloads and deletes a document", async ({
  page,
}) => {
  const client = E2E.documentsClient;
  const cspViolations: string[] = [];
  page.on("console", (message) => {
    if (/Content Security Policy/i.test(message.text())) {
      cspViolations.push(message.text());
    }
  });

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
    await page.getByLabel(t("SelectFiles"), { exact: true }).setInputFiles([pdf, csv]);
    await expect(page.getByRole("list", { name: t("app.documents.selected") })).toContainText(
      pdf.name,
    );
    await page.getByRole("combobox", { name: `${t("Client")} *` }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill("Russo");
    await page.getByRole("option", { name: /Russo Elena/ }).click();
    await expectAccessible(page, "document uploader");
    await page.getByRole("button", { name: t("Upload"), exact: true }).click();
    await expect(toast(page, t("app.documents.uploaded"))).toBeVisible();
  });

  await test.step("the client's 360° tab lists it", async () => {
    await page.goto(`/${E2E.tenant}/clients/${client.id}?tab=documents`);
    const table = page.getByRole("table", { name: t("app.documents.title") });
    await expect(table).toContainText("Contratto_firmato.pdf");
    await expectAccessible(page, "client documents");
    await expectNoHorizontalScroll(page);
  });

  await test.step("the drawer shows the beginning of a text file", async () => {
    await page.getByRole("button", { name: "Elenco.csv" }).click();
    const drawer = page.getByRole("dialog");
    await expect(
      drawer.getByRole("region", { name: t("app.documents.viewer.title", { name: "Elenco.csv" }) }),
    ).toContainText("Rossi;Forlì");
    await expectAccessible(page, "text preview");
    await drawer.getByRole("button", { name: t("Delete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(toast(page, t("app.documents.deleted"))).toBeVisible();
  });

  await test.step("the drawer draws the PDF with PDF.js", async () => {
    // The worker comes from the app with its own CSP (ADR 0020), not as a fallback in the page.
    const worker = page.waitForResponse(/\/static\/pdfjs\/pdf\.worker\.min\.mjs$/);
    await page.getByRole("button", { name: "Contratto_firmato.pdf" }).click();
    expect((await worker).headers()["content-security-policy"]).toBe(
      "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'",
    );
    const drawer = page.getByRole("dialog");
    const canvas = drawer.getByRole("img", {
      name: t("app.documents.viewer.pageOf", { page: 1, pages: 1 }),
    });
    await expect(canvas).toBeVisible();
    // Drawn, not just laid out: some pixels of the page are dark (the text).
    await expect
      .poll(() =>
        canvas.evaluate((element: HTMLCanvasElement) => {
          const { data } = element
            .getContext("2d")!
            .getImageData(0, 0, element.width, element.height);
          let dark = 0;
          for (let index = 0; index < data.length; index += 4) {
            // Opaque and dark: a transparent pixel (0, 0, 0, 0) is not ink.
            dark += data[index + 3]! > 200 && data[index]! < 128 ? 1 : 0;
          }
          return dark;
        }),
      )
      .toBeGreaterThan(50);
    await expectAccessible(page, "pdf preview");

    // The file opened in a tab keeps the API's CSP through the BFF.
    const href = await drawer
      .getByRole("link", { name: t("app.documents.viewer.newTab") })
      .getAttribute("href");
    const inline = await page.request.get(href!);
    expect(inline.headers()["content-disposition"]).toMatch(/^inline/);
    expect(inline.headers()["content-security-policy"]).toBe(
      "default-src 'none'; frame-ancestors 'none'",
    );
    expect(cspViolations).toEqual([]);
  });

  await test.step("the drawer renames it, keeping the extension, and downloads it", async () => {
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
