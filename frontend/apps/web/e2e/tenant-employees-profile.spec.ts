import { expect, test, type Page } from "@playwright/test";

import { BASE_URL, E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Employee pages and own profile (B-05, F04, F06): an Administrator creates an employee, edits it, gives it an
 * administrator and a client, disables its sign-in and deletes it; an Employee changes their profile (data, picture,
 * theme, language, password) and finds language and theme again at the next sign-in from a clean browser.
 */

/** A 2 × 2 PNG: enough for the server to decode, resize and re-encode. */
const PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGPQ6w4HIgYIBQAdKgRBwvFrKQAAAABJRU5ErkJggg==",
  "base64",
);

/** Creates a client of nobody through the BFF, as the signed-in Administrator (the browser's session cookie). */
async function createClient(page: Page, lastName: string): Promise<string> {
  const response = await page.request.post("/api/bff/clients", {
    headers: { "x-requested-with": "auxilia", origin: BASE_URL },
    data: {
      firstName: "Carla",
      lastName,
      birthDate: "1979-09-09",
      email: `${lastName.toLowerCase()}@example.test`,
      phone: null,
      fiscalCode: fiscalCode(),
      customFields: null,
      employeeUserId: null,
    },
  });
  expect(response.status(), await response.text()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

/** A valid fiscal code nobody else has (random letters and digits in the right places). */
function fiscalCode(): string {
  const letter = () => String.fromCharCode(65 + Math.floor(Math.random() * 26));
  const digit = () => String(Math.floor(Math.random() * 10));
  const l = (n: number) => Array.from({ length: n }, letter).join("");
  const d = (n: number) => Array.from({ length: n }, digit).join("");
  return `${l(6)}${d(2)}A${d(2)}${letter()}${d(3)}${letter()}`;
}

test("an Administrator creates an employee and manages it", async ({ page }) => {
  const stamp = Date.now();
  const lastName = `Russo${stamp}`;
  const email = `marco.${stamp}@example.test`;
  const clientLastName = `Ferri${stamp}`;

  await test.step("the list is accessible and the form validates", async () => {
    await signInWithNewPassword(page, E2E.administrator.userName);
    await page
      .getByRole("navigation", { name: t("app.shell.navigation") })
      .getByRole("link", { name: t("nav.employees") })
      .click();
    await expect(page.getByRole("heading", { name: t("Employees"), level: 1 })).toBeVisible();
    await expectAccessible(page, "employees");

    await page.getByRole("link", { name: t("CreateNewEmployee") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/employees/new`);
    await page.getByRole("button", { name: t("CreateNewEmployee") }).click();
    await expect(page.getByText(t("validation.person.birthDate"))).toBeVisible();
    await expectAccessible(page, "new employee");
  });

  await test.step("the employee is created and found in the list", async () => {
    await page.getByLabel(`${t("Name")} *`, { exact: true }).fill("Marco");
    await page.getByLabel(`${t("Surname")} *`, { exact: true }).fill(lastName);
    await page.getByLabel(`${t("BirthDate")} *`).fill("1990-02-10");
    await page.getByLabel(`${t("Email")} *`).fill(email);
    await page.getByRole("button", { name: t("CreateNewEmployee") }).click();
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/employees/[0-9a-f-]{36}`));
    await expect(page.getByRole("heading", { name: `Marco ${lastName}`, level: 1 })).toBeVisible();
    await expectAccessible(page, "employee overview");

    await page.getByRole("link", { name: t("app.employees.back") }).click();
    await page.getByLabel(t("Email"), { exact: true }).fill(email);
    const table = page.getByRole("table", { name: t("Employees") });
    await expect(table).toContainText(lastName);
    await expect(table).toContainText(t("Active"));
    await table.getByRole("link", { name: lastName }).click();
  });

  await test.step("data and administrator are edited", async () => {
    await page.getByRole("tab", { name: t("app.clients.tabs.data") }).click();
    await page.getByLabel(t("Phone")).fill("06 9876 5432");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.clients.saved"))).toBeVisible();
    await expectAccessible(page, "employee data");

    await page.getByRole("tab", { name: t("Overview") }).click();
    await page.getByRole("combobox", { name: t("Administrator") }).click();
    await page.getByRole("option", { name: E2E.administrator.fullName }).click();
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.clients.saved"))).toBeVisible();
  });

  await test.step("a client is assigned and removed", async () => {
    await createClient(page, clientLastName);
    await page.getByRole("tab", { name: t("AssignedClients") }).click();
    await page.getByRole("combobox", { name: t("Client") }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill(clientLastName);
    await page.getByRole("option", { name: new RegExp(clientLastName) }).click();
    await page.getByRole("button", { name: t("app.clients.assign") }).click();
    await expect(toast(page, t("app.employees.clientAssigned"))).toBeVisible();
    const clients = page.getByRole("table", { name: t("AssignedClients") });
    await expect(clients).toContainText(clientLastName);
    await expectAccessible(page, "employee clients");

    await clients.getByRole("button", { name: new RegExp(clientLastName) }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.employees.unassign") })
      .click();
    await expect(toast(page, t("app.employees.clientUnassigned"))).toBeVisible();
    await expect(clients).not.toContainText(clientLastName);
  });

  await test.step("sign-in is disabled and the employee deleted", async () => {
    await page.getByRole("tab", { name: t("app.clients.tabs.access") }).click();
    await page.getByRole("switch", { name: t("app.clients.canSignIn") }).click();
    await expect(toast(page, t("app.clients.signInDisabledToast"))).toBeVisible();
    await expect(page.getByText(t("Inactive"), { exact: true })).toBeVisible();
    await expectAccessible(page, "employee account");

    await page.getByRole("button", { name: t("Delete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(page).toHaveURL(new RegExp(`/${E2E.tenant}/employees(\\?.*)?$`));
    await page.getByLabel(t("Email"), { exact: true }).fill(email);
    await expect(page.getByRole("table", { name: t("Employees") })).not.toContainText(lastName);
  });
});

test("an Employee changes their profile and finds it again at the next sign-in", async ({
  page,
}) => {
  const user = E2E.profileUser;
  let password = "";

  await test.step("the profile opens from the account menu", async () => {
    password = await signInWithNewPassword(page, user.userName);
    await page.getByRole("button", { name: t("app.shell.accountMenu") }).click();
    await page.getByRole("menuitem", { name: t("MyProfile") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/profile`);
    await expect(page.getByRole("heading", { name: t("MyProfile"), level: 1 })).toBeVisible();
    await expect(page.getByLabel(t("Username"))).toHaveValue(user.userName);
    await expectAccessible(page, "profile");
  });

  await test.step("personal data are validated and saved", async () => {
    await page.getByLabel(t("Phone")).fill("12");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(page.getByText(t("validation.person.phone"))).toBeVisible();
    await page.getByLabel(t("Phone")).fill("333 7654321");
    await page.getByRole("button", { name: t("Save") }).click();
    await expect(toast(page, t("app.profile.saved"))).toBeVisible();
  });

  await test.step("the picture is uploaded, shown in the header and removed", async () => {
    await page.locator("#profile-picture-file").setInputFiles({
      name: "me.png",
      mimeType: "image/png",
      buffer: PNG,
    });
    await expect(toast(page, t("app.profile.imageSaved"))).toBeVisible();
    const header = page.getByRole("button", { name: t("app.shell.accountMenu") });
    await expect(header.locator("img")).toHaveAttribute("src", /\/api\/bff\/users\/.+\/image\?v=/);
    await expectAccessible(page, "profile with picture");

    await page.getByRole("button", { name: t("app.profile.imageDelete") }).click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Delete") })
      .click();
    await expect(toast(page, t("app.profile.imageDeleted"))).toBeVisible();
    await expect(page.getByRole("button", { name: t("app.profile.imageDelete") })).toHaveCount(0);
  });

  await test.step("theme and language apply at once", async () => {
    await page.getByRole("combobox", { name: t("app.profile.theme") }).click();
    await page.getByRole("option", { name: t("common.theme.dark") }).click();
    await expect(page.locator("html")).toHaveClass(/dark/);
    await expect(page.getByRole("combobox", { name: t("app.profile.theme") })).toHaveText(
      t("common.theme.dark"),
    );
    await expectAccessible(page, "profile (dark)");

    await page.getByRole("combobox", { name: t("Language") }).click();
    await page.getByRole("option", { name: "English" }).click();
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
  });

  await test.step("the password needs the current one and its confirmation", async () => {
    // The page is in English now: the fields are found by their ids.
    const next = `E2e-Pw-${Date.now()}-new!`;
    await page
      .locator("form", { has: page.locator('[autocomplete="current-password"]') })
      .evaluate((form) => form.scrollIntoView());
    const current = page.locator('input[autocomplete="current-password"]');
    const fresh = page.locator('input[autocomplete="new-password"]');
    await current.fill("wrong-password");
    await fresh.nth(0).fill(next);
    await fresh.nth(1).fill(`${next}x`);
    await page
      .locator('form:has(input[autocomplete="current-password"]) button[type="submit"]')
      .click();
    await expect(fresh.nth(1)).toHaveAttribute("aria-invalid", "true");
    await fresh.nth(1).fill(next);
    await page
      .locator('form:has(input[autocomplete="current-password"]) button[type="submit"]')
      .click();
    await expect(current).toHaveAttribute("aria-invalid", "true");
    await current.fill(password);
    await page
      .locator('form:has(input[autocomplete="current-password"]) button[type="submit"]')
      .click();
    await expect(current).toHaveValue("");
    password = next;
  });

  await test.step("language and theme come back at the next sign-in", async () => {
    await page.context().clearCookies();
    await page.evaluate(() => window.localStorage.clear());
    await page.goto(`/${E2E.tenant}/login`);
    await expect(page.locator("html")).not.toHaveClass(/dark/);
    await page.getByLabel(t("Username")).fill(user.userName);
    await page.getByLabel(t("Password"), { exact: true }).fill(password);
    await page.getByRole("button", { name: t("Login") }).click();
    await expect(page).toHaveURL(`/${E2E.tenant}/dashboard`);
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
    await expect(page.locator("html")).toHaveClass(/dark/);

    // Back to Italian and the system theme, so the next run starts from the seeded profile.
    await page.goto(`/${E2E.tenant}/profile`);
    await page.locator("#profile-language").click();
    await page.getByRole("option", { name: "Italiano" }).click();
    await expect(page.locator("html")).toHaveAttribute("lang", "it");
    await page.getByRole("combobox", { name: t("app.profile.theme") }).click();
    await page.getByRole("option", { name: t("common.theme.system") }).click();
    await expect(toast(page, t("app.profile.themeSaved"))).toBeVisible();

    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
    await expectAccessible(page, "profile (phone)");
  });
});
