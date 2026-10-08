import { expect, test, type Page } from "@playwright/test";

import { E2E } from "./support/env";
import { signInWithNewPassword, toast } from "./support/sign-in";
import { expectAccessible, expectNoHorizontalScroll, t } from "./support/ui";

/*
 * Appointment pages (B-17, F13): an employee schedules an appointment for a client in her charge; the client sees it,
 * cancels it and requests another one; the employee approves and completes the request and finds it in the client's
 * 360° tab. Calendar, grid and drawer are checked with axe, the calendar on a phone too.
 */

/** A day a few days ahead (`YYYY-MM-DD`), clear of any time-zone edge. */
const day = (offset: number) => {
  const value = new Date();
  value.setUTCDate(value.getUTCDate() + offset);
  return value.toISOString().slice(0, 10);
};

/**
 * The days of this run: a few days ahead plus the minute of the run, so a run on the same database never finds the
 * appointments of a previous one at the same day and time.
 */
const runOffset = 3 + (Math.floor(Date.now() / 60_000) % 600);
const scheduledDay = day(runOffset);
const requestedDay = day(runOffset + 1);

/** The text of the list button of an appointment (date as the Italian bundle formats it, and time). */
const slot = (date: string, time: string) =>
  `${new Intl.DateTimeFormat("it-IT", { dateStyle: "medium" }).format(new Date(`${date}T${time}`))} ${time}`;

const drawer = (page: Page) => page.getByRole("dialog", { name: t("ManageAppointment") });

const openAppointments = async (page: Page) => {
  await page
    .getByRole("navigation", { name: t("app.shell.navigation") })
    .getByRole("link", { name: t("nav.appointments") })
    .click();
  await expect(page.getByRole("heading", { name: t("nav.appointments"), level: 1 })).toBeVisible();
};

const showList = async (page: Page) => {
  await page.getByRole("button", { name: t("AppointmentsList") }).click();
  await expect(page.getByRole("table", { name: t("nav.appointments") })).toBeVisible();
};

test("an employee and a client run an appointment from request to completion", async ({
  page,
  browser,
}) => {
  const employee = E2E.appointmentsEmployee;
  const client = E2E.appointmentsClient;

  await test.step("the employee's calendar is accessible, on a phone too", async () => {
    await signInWithNewPassword(page, employee.userName);
    await openAppointments(page);
    await expect(page.getByRole("button", { name: t("AppointmentNew") })).toBeVisible();
    await expect(page.locator(".fc")).toBeVisible();
    await expectAccessible(page, "appointments calendar");
    await page.setViewportSize({ width: 360, height: 780 });
    await expectNoHorizontalScroll(page);
    await expectAccessible(page, "appointments calendar mobile");
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step("the employee schedules an appointment for her client", async () => {
    await page.getByRole("button", { name: t("AppointmentNew") }).click();
    const dialog = page.getByRole("dialog", { name: t("AppointmentNew") });
    await dialog.getByRole("combobox", { name: `${t("Client")} *` }).click();
    await page.getByPlaceholder(t("common.combobox.search")).fill("Conti");
    await page.getByRole("option", { name: /Conti Giulia/ }).click();
    await dialog.getByLabel(`${t("Date")} *`).fill(scheduledDay);
    await dialog.getByLabel(`${t("Time")} *`).fill("10:00");
    await dialog.getByLabel(`${t("DurationMinutes")} *`).fill("45");
    await dialog.getByLabel(t("Notes")).fill("Prima visita");
    await expectAccessible(page, "new appointment");
    await dialog.getByRole("button", { name: t("Create") }).click();
    await expect(toast(page, t("app.appointments.scheduled"))).toBeVisible();
    await expect(drawer(page)).toContainText(t("Approved"));
    await expect(drawer(page)).toContainText("Prima visita");
    await expectAccessible(page, "appointment drawer");
    await page.keyboard.press("Escape");
  });

  const clientPage = await (await browser.newContext()).newPage();

  await test.step("the client sees it, cancels it and requests another one", async () => {
    await signInWithNewPassword(clientPage, client.userName);
    await openAppointments(clientPage);
    await showList(clientPage);
    const table = clientPage.getByRole("table", { name: t("nav.appointments") });
    await expect(table).toContainText(employee.fullName);
    await expectAccessible(clientPage, "client appointments");

    await table.getByRole("button", { name: slot(scheduledDay, "10:00") }).click();
    await expect(drawer(clientPage)).toContainText(t("Approved"));
    await expect(drawer(clientPage).getByRole("button", { name: t("Approve") })).toHaveCount(0);
    await drawer(clientPage)
      .getByRole("button", { name: t("app.appointments.cancel") })
      .click();
    await clientPage
      .getByRole("alertdialog")
      .getByRole("button", { name: t("app.appointments.cancel") })
      .click();
    await expect(toast(clientPage, t("app.appointments.cancelled"))).toBeVisible();
    await expect(drawer(clientPage)).toContainText(t("Cancelled"));
    await clientPage.keyboard.press("Escape");

    await clientPage.getByRole("button", { name: t("RequestAppointment") }).click();
    const dialog = clientPage.getByRole("dialog", { name: t("RequestAppointment") });
    await expect(
      dialog.getByRole("combobox", { name: `${t("app.appointments.operator")} *` }),
    ).toContainText(employee.fullName);
    await dialog.getByLabel(`${t("Date")} *`).fill(requestedDay);
    await dialog.getByLabel(`${t("Time")} *`).fill("15:00");
    await expectAccessible(clientPage, "request appointment");
    await dialog.getByRole("button", { name: t("RequestAppointment") }).click();
    await expect(toast(clientPage, t("app.appointments.requested"))).toBeVisible();
    await expect(drawer(clientPage)).toContainText(t("Pending"));
    await clientPage.context().close();
  });

  await test.step("the employee approves and completes the request", async () => {
    await page.reload();
    await showList(page);
    const table = page.getByRole("table", { name: t("nav.appointments") });
    await table.getByRole("button", { name: slot(requestedDay, "15:00") }).click();
    await expect(drawer(page)).toContainText(t("app.appointments.requestedByClient"));
    await drawer(page)
      .getByRole("button", { name: t("Approve"), exact: true })
      .click();
    await expect(toast(page, t("app.appointments.approved"))).toBeVisible();
    await drawer(page)
      .getByRole("button", { name: t("Complete"), exact: true })
      .click();
    await page
      .getByRole("alertdialog")
      .getByRole("button", { name: t("Complete") })
      .click();
    await expect(toast(page, t("app.appointments.completed"))).toBeVisible();
    await expect(
      drawer(page)
        .getByRole("list", { name: t("app.appointments.history") })
        .getByRole("listitem"),
    ).toHaveCount(3);
    await page.keyboard.press("Escape");
  });

  await test.step("the client's 360° tab shows the appointments", async () => {
    await page.goto(`/${E2E.tenant}/clients/${client.id}?tab=appointments&view=list`);
    const table = page.getByRole("table", { name: t("nav.appointments") });
    await expect(
      table.getByRole("row").filter({ hasText: slot(scheduledDay, "10:00") }),
    ).toContainText(t("Cancelled"));
    await expect(
      table.getByRole("row").filter({ hasText: slot(requestedDay, "15:00") }),
    ).toContainText(t("Completed"));
    await expectAccessible(page, "client appointments tab");
  });
});
