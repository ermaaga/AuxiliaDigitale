import { expect, test } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Marketing pages (M-04, N01): an Administrator builds a segment with its live count, creates a static list, writes a
 * template with its preview, and prepares a campaign draft with the wizard (no Worker in the E2E run: it is not sent).
 */
test("an Administrator prepares a campaign", async ({ page }) => {
  test.setTimeout(120_000);
  const suffix = Date.now().toString(36);

  await test.step("the segment builder counts the clients while the rule changes", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page.goto(`/${E2E.tenant}/marketing`);
    await expect(page).toHaveURL(/\/marketing\/campaigns$/);
    await expect(page.getByRole("heading", { name: t("nav.marketing"), level: 1 })).toBeVisible();
    await expectAccessible(page, "campaigns");

    await page.getByRole("link", { name: t("app.marketing.nav.segments") }).click();
    await page.getByRole("link", { name: t("app.marketing.segments.new") }).click();
    await page.getByLabel(t("Name")).fill(`Attivi ${suffix}`);
    await page.getByRole("combobox", { name: t("app.marketing.segments.value") }).click();
    await page.getByRole("option", { name: t("Active"), exact: true }).click();
    await expect(page.getByText(/\d+ clienti/).first()).toBeVisible();
    await page.getByRole("button", { name: t("app.marketing.segments.addGroup") }).click();
    await expect(
      page.getByRole("group", { name: t("app.marketing.segments.group", { number: 1 }) }),
    ).toBeVisible();
    await page.getByRole("button", { name: t("app.marketing.segments.removeGroup") }).click();
    await expectAccessible(page, "segment builder");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("app.marketing.segments.saved"))).toBeVisible();
    await expect(page).toHaveURL(/\/marketing\/segments\/[0-9a-f-]{36}$/);
  });

  await test.step("a static list is created", async () => {
    await page.goto(`/${E2E.tenant}/marketing/lists`);
    await page.getByRole("button", { name: t("app.marketing.lists.new") }).click();
    await page
      .getByRole("dialog")
      .getByLabel(`${t("Name")} *`)
      .fill(`Newsletter ${suffix}`);
    await page
      .getByRole("dialog")
      .getByRole("button", { name: t("Save") })
      .click();
    await expect(page).toHaveURL(/\/marketing\/lists\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { name: `Newsletter ${suffix}` })).toBeVisible();
    await expectAccessible(page, "static list");
  });

  await test.step("a template is written and previewed", async () => {
    await page.goto(`/${E2E.tenant}/marketing/templates/new`);
    await page.getByLabel(`${t("Name")} *`).fill(`Primavera ${suffix}`);
    await page.getByLabel(`${t("Subject")} *`).fill("Ciao {{ firstName }}");
    await page
      .getByLabel(`${t("app.marketing.templates.body")} *`)
      .fill("<p>Ciao {{ fullName }}, novità da {{ tenantName }}.</p>");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(page).toHaveURL(/\/marketing\/templates\/[0-9a-f-]{36}$/);
    await expect(page.getByText("Ciao Mario")).toBeVisible();
    // axe runs before the preview opens: the sandboxed frame (no scripts) cannot host it.
    await expectAccessible(page, "template editor");
    await page.getByRole("button", { name: t("app.marketing.templates.showPreview") }).click();
    await expect(
      page
        .frameLocator(`iframe[title="${t("app.marketing.templates.preview")}"]`)
        .getByText(/Ciao Mario/),
    ).toBeVisible();
  });

  await test.step("the wizard saves a campaign draft", async () => {
    await page.goto(`/${E2E.tenant}/marketing/campaigns/new`);
    await page.getByLabel(`${t("Name")} *`).fill(`Primavera ${suffix}`);
    await page
      .getByRole("combobox", { name: `${t("app.marketing.campaigns.audience")} *` })
      .click();
    await page
      .getByRole("option", {
        name: t("app.marketing.campaigns.segmentOption", { name: `Attivi ${suffix}` }),
      })
      .click();
    await page.getByRole("button", { name: t("app.marketing.campaigns.next") }).click();
    await page
      .getByRole("combobox", { name: `${t("app.marketing.campaigns.template")} *` })
      .click();
    await page.getByRole("option", { name: new RegExp(`Primavera ${suffix}`) }).click();
    await expect(page.getByText("Ciao Mario")).toBeVisible();
    await expectAccessible(page, "campaign wizard");
    await page.getByRole("button", { name: t("app.marketing.campaigns.next") }).click();
    await page.getByRole("button", { name: t("app.marketing.campaigns.createDraft") }).click();
    await expect(page).toHaveURL(/\/marketing\/campaigns\/[0-9a-f-]{36}$/);
    await expect(
      page.getByText(t("app.marketing.campaigns.status.Draft"), { exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: t("app.marketing.campaigns.send") }),
    ).toBeVisible();
    await expectAccessible(page, "campaign draft");
    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
  });
});
