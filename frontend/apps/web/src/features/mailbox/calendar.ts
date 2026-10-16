import type { EventInput } from "@fullcalendar/core";

import type { ConnectedAccount, ExternalCalendarEvent } from "./api";
import { accountColorVar } from "./colors";

/** Where the browser remembers the accounts hidden from the calendar (a per-viewer convenience). */
const HIDDEN_STORAGE_KEY = "aux.calendar.hiddenAccounts";

/** The accounts whose calendar is shown in the appointment calendar (N05 #101). */
export function calendarAccounts(accounts: readonly ConnectedAccount[] | undefined) {
  return (accounts ?? []).filter((account) => account.hasCalendar);
}

/** The name of an account in the legend and in the details of an event. */
export function accountName(account: Pick<ConnectedAccount, "displayName" | "address">) {
  return account.displayName?.trim() || account.address;
}

/** A wall-clock time of the API (`HH:mm` or `HH:mm:ss[.fff]`) as FullCalendar reads it in UTC (`HH:mm:ss`). */
function clock(time: string) {
  return time.length === 5 ? `${time}:00` : time.slice(0, 8);
}

/**
 * The copied events of the visible accounts as FullCalendar events, in the account colour and read only. The times
 * are the tenant's wall clock written as UTC like the appointments (the calendar runs in `timeZone: "UTC"`); the end
 * of an all-day event is exclusive in both the API and FullCalendar.
 */
export function toCalendarEvents(
  events: readonly ExternalCalendarEvent[] | undefined,
  accounts: readonly ConnectedAccount[],
  hidden: ReadonlySet<string>,
): EventInput[] {
  const byId = new Map(accounts.map((account) => [account.id, account]));
  return (events ?? []).flatMap((item) => {
    const account = byId.get(item.accountId);
    if (!account || hidden.has(account.id)) {
      return [];
    }

    const color = accountColorVar(account.color);
    return [
      {
        id: `external-${item.id}`,
        title: item.title,
        allDay: item.allDay,
        start: item.allDay || !item.time ? item.date : `${item.date}T${clock(item.time)}`,
        end: item.allDay || !item.endTime ? item.endDate : `${item.endDate}T${clock(item.endTime)}`,
        editable: false,
        startEditable: false,
        durationEditable: false,
        classNames: ["external-event", ...(item.tentative ? ["external-event-tentative"] : [])],
        borderColor: color,
        backgroundColor: `color-mix(in oklab, ${color} 18%, var(--background))`,
        textColor: "var(--foreground)",
        extendedProps: { external: item },
      },
    ];
  });
}

/** The link to the event at the provider: only `https` (the API keeps nothing else; checked again before rendering). */
export function safeEventLink(link: string | null | undefined): string | undefined {
  if (!link) {
    return undefined;
  }

  try {
    return new URL(link).protocol === "https:" ? link : undefined;
  } catch {
    return undefined;
  }
}

export function loadHiddenAccounts(): Set<string> {
  try {
    const stored = JSON.parse(window.localStorage.getItem(HIDDEN_STORAGE_KEY) ?? "[]") as unknown;
    return new Set(
      Array.isArray(stored) ? stored.filter((id): id is string => typeof id === "string") : [],
    );
  } catch {
    return new Set();
  }
}

export function saveHiddenAccounts(hidden: ReadonlySet<string>) {
  try {
    window.localStorage.setItem(HIDDEN_STORAGE_KEY, JSON.stringify([...hidden]));
  } catch {
    // Storage blocked (private window, disabled site data): the choice lasts for this page only.
  }
}
