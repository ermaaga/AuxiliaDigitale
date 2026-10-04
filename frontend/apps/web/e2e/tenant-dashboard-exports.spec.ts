import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Dashboard, exports and clients overview (B-24, F27, F26, F07): an Administrator sees the dashboard of the role with
 * its period, exports the clients as CSV and the services as Excel from their tables, and the selected clients of the
 * overview as PDF.
 */

const exportAs = async (
  page: Page,
  format: "CSV" | "Excel" | "PDF",
  button = t("common.table.export"),
) => {
  const downloading = page.waitForEvent("download");
  await page.getByRole("button", { name: button }).click();
  await page.getByRole("menuitem", { name: format }).click();
  return downloading;
};

test("an Administrator reads the dashboard and exports lists", async ({ page }) => {
  await test.step("the dashboard shows the figures of the role", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await expect(
      page.getByRole("heading", { name: new RegExp(t("Welcome")), level: 1 }),
    ).toBeVisible();
    const figures = page.getByRole("region", { name: t("app.dashboard.figures") });
    await expect(figures.getByText(t("app.dashboard.totalClients"))).toBeVisible();
    await expect(figures.getByText(t("app.dashboard.openCases"))).toBeVisible();
    await page.getByRole("combobox", { name: t("app.dashboard.period") }).click();
    await page.getByRole("option", { name: t("app.dashboard.periods.year") }).click();
    await expect(page).toHaveURL(/period=year/);
    await expect(page.getByText(t("app.dashboard.casesPerService"))).toBeVisible();
    await expectAccessible(page, "dashboard");
    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step("the clients export as CSV", async () => {
    await page.goto(`/${E2E.tenant}/clients?view=all`);
    const file = await exportAs(page, "CSV");
    expect(file.suggestedFilename()).toMatch(/_\d{8}_\d{6}\.csv$/);
    const text = await (await file.createReadStream()).toArray();
    expect(Buffer.concat(text).toString("utf8")).toContain(t("Surname"));
  });

  await test.step("the services export as Excel", async () => {
    await page.goto(`/${E2E.tenant}/services`);
    const file = await exportAs(page, "Excel");
    expect(file.suggestedFilename()).toMatch(/\.xlsx$/);
  });

  await test.step("the overview exports the selected clients as PDF", async () => {
    await page.goto(`/${E2E.tenant}/clients`);
    await page.getByRole("link", { name: t("app.clients.overview.title") }).click();
    await expect(
      page.getByRole("heading", { name: t("app.clients.overview.title"), level: 1 }),
    ).toBeVisible();
    const table = page.getByRole("table", { name: t("app.clients.overview.title") });
    await table.getByRole("checkbox").first().check();
    await expect(
      page.getByRole("button", { name: t("app.clients.overview.clearSelection", { count: 1 }) }),
    ).toBeVisible();
    await expectAccessible(page, "clients overview");
    const file = await exportAs(page, "PDF", t("app.clients.overview.export"));
    expect(file.suggestedFilename()).toMatch(/\.pdf$/);
  });

  await test.step("the exports page is accessible", async () => {
    await page.goto(`/${E2E.tenant}/exports`);
    await expect(
      page.getByRole("heading", { name: t("app.exports.title"), level: 1 }),
    ).toBeVisible();
    await expectAccessible(page, "exports");
  });
});
