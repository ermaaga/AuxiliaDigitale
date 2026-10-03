import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Service pages (B-15, F08, F33): an Administrator adds a category, creates a service in it, builds its folder
 * template (root folders, a subfolder, reorder, rename, delete), edits and deactivates it, finds it in the filtered
 * list and deletes it.
 */

const folders = (page: Page) => page.getByRole("list", { name: t("FolderTemplate") });

/** The header of the detail page (the form repeats the category and the status). */
const header = (page: Page) =>
  page.locator("header").filter({ has: page.getByRole("heading", { level: 1 }) });

const addFolder = async (page: Page, button: string, name: string) => {
  await page.getByRole("button", { name: button }).click();
  await page.getByRole("textbox", { name: t("FolderName") }).fill(name);
  await page.getByRole("textbox", { name: t("FolderName") }).press("Enter");
  await expect(folders(page).getByText(name, { exact: true })).toBeVisible();
};

test("an Administrator manages a service and its folder template", async ({ page }) => {
  const suffix = Date.now().toString(36);
  const category = `Fiscale ${suffix}`;
  const service = `Successione ${suffix}`;

  await test.step("the service list is accessible", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.services") })
      .click();
    await expect(page.getByRole("heading", { name: t("nav.services"), level: 1 })).toBeVisible();
    await expect(page.getByRole("table", { name: t("nav.services") })).toContainText(
      E2E.caseService.name,
    );
    await expectAccessible(page, "services");
  });

  await test.step("a category is added", async () => {
    await page.getByRole("button", { name: t("app.services.categories") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(t("app.services.newCategory")).fill(category);
    await dialog.getByRole("button", { name: t("Add") }).click();
    await expect(toast(page, t("app.services.categorySaved")).first()).toBeVisible();
    await expect(
      dialog.getByRole("textbox", { name: t("app.services.renameCategory", { name: category }) }),
    ).toHaveValue(category);
    await expectAccessible(page, "service categories");
    await page.keyboard.press("Escape");
  });

  await test.step("a service is created in the category", async () => {
    await page.getByRole("button", { name: t("app.services.new") }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(`${t("Name")} *`).fill(service);
    await dialog.getByLabel(`${t("Price")} (€) *`).fill("1.234");
    await dialog.getByLabel(`${t("DurationDays")} *`).fill("60");
    await dialog.getByRole("combobox", { name: t("app.services.category") }).click();
    await page.getByRole("option", { name: category }).click();
    await dialog.getByRole("button", { name: t("Create") }).click();
    await expect(dialog.getByText(t("validation.services.price"))).toBeVisible();
    await dialog.getByLabel(`${t("Price")} (€) *`).fill("250,50");
    await expectAccessible(page, "new service");
    await dialog.getByRole("button", { name: t("Create") }).click();
    await expect(toast(page, t("app.services.created"))).toBeVisible();
    await expect(page.getByRole("heading", { name: service, level: 1 })).toBeVisible();
    await expect(header(page).getByText(category)).toBeVisible();
    await expectAccessible(page, "service detail");
  });

  await test.step("the folder template is built", async () => {
    await page.getByRole("tab", { name: t("FolderTemplate") }).click();
    await expect(page.getByText(t("NoFoldersDefined"))).toBeVisible();
    await addFolder(page, t("AddRootFolder"), "Anagrafica");
    await addFolder(page, t("AddRootFolder"), "Redditi");
    await addFolder(page, `${t("AddSubfolder")}: Anagrafica`, "Documenti");
    await expect(folders(page).getByRole("listitem")).toHaveText([
      "Anagrafica",
      "Documenti",
      "Redditi",
    ]);

    await page
      .getByRole("button", { name: t("app.services.moveFolderUp", { name: "Redditi" }) })
      .click();
    await expect(folders(page).getByRole("listitem")).toHaveText([
      "Redditi",
      "Anagrafica",
      "Documenti",
    ]);

    await page.getByRole("button", { name: `${t("EditFolderName")}: Documenti` }).click();
    const rename = page.getByRole("textbox", { name: t("EditFolderName") });
    await rename.fill("Identità");
    await rename.press("Enter");
    await expect(toast(page, t("app.services.folderRenamed"))).toBeVisible();
    await expect(folders(page).getByText("Identità", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: `${t("DeleteFolder")}: Redditi` }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(toast(page, t("app.services.folderDeleted"))).toBeVisible();
    await expect(folders(page).getByRole("listitem")).toHaveText(["Anagrafica", "Identità"]);
    await expectAccessible(page, "folder template");
  });

  await test.step("the service is edited and deactivated", async () => {
    await page.getByRole("tab", { name: t("app.services.tabs.data") }).click();
    await page.getByLabel(`${t("DurationDays")} *`).fill("90");
    await page.getByRole("switch", { name: t("Status") }).click();
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.services.saved"))).toBeVisible();
    await expect(header(page).getByText(t("Inactive"))).toBeVisible();

    await page.getByRole("tab", { name: t("nav.cases") }).click();
    await expect(page.getByRole("table", { name: t("nav.cases") })).toBeVisible();
    await expectAccessible(page, "service cases");
  });

  await test.step("the list filters it and works on a phone", async () => {
    await page.getByRole("link", { name: t("app.services.back") }).click();
    await page.locator("#services-name").fill(service);
    const table = page.getByRole("table", { name: t("nav.services") });
    await expect(table.getByRole("row")).toHaveCount(2);
    await expect(table).toContainText(t("Inactive"));
    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
    await expectAccessible(page, "services mobile");
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step("the service is deleted", async () => {
    await page.getByRole("link", { name: service }).click();
    await page.getByRole("button", { name: t("Delete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(toast(page, t("app.services.deleted"))).toBeVisible();
    await expect(page.getByRole("heading", { name: t("nav.services"), level: 1 })).toBeVisible();
  });
});
