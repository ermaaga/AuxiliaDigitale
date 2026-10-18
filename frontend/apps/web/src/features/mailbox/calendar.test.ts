import { afterEach, describe, expect, it, vi } from "vitest";

import { isSyncActive, type ConnectedAccount, type ExternalCalendarEvent } from "./api";
import {
  accountName,
  calendarAccounts,
  loadHiddenAccounts,
  safeEventLink,
  saveHiddenAccounts,
  toCalendarEvents,
} from "./calendar";

const account = (id: string, input: Partial<ConnectedAccount> = {}): ConnectedAccount => ({
  id,
  provider: "google",
  address: `${id}@example.com`,
  displayName: null,
  color: "blue",
  hasMail: false,
  hasCalendar: true,
  status: "Active",
  lastErrorCode: null,
  imapHost: null,
  imapPort: null,
  connectedAt: "2026-10-01T08:00:00Z",
  mailSyncedAt: null,
  calendarSyncedAt: null,
  ...input,
});

const event = (input: Partial<ExternalCalendarEvent> = {}): ExternalCalendarEvent => ({
  id: "e1",
  accountId: "a1",
  title: "Riunione",
  location: null,
  startsAt: "2026-10-20T07:30:00Z",
  endsAt: "2026-10-20T08:00:00Z",
  date: "2026-10-20",
  time: "09:30:00",
  endDate: "2026-10-20",
  endTime: "10:00:00",
  allDay: false,
  tentative: false,
  link: null,
  ...input,
});

describe("connected calendars in the appointment calendar", () => {
  it("keeps the accounts with the calendar and names them", () => {
    const accounts = [account("a1"), account("a2", { hasCalendar: false })];
    expect(calendarAccounts(accounts).map((item) => item.id)).toEqual(["a1"]);
    expect(calendarAccounts(undefined)).toEqual([]);
    expect(accountName(account("a1", { displayName: " Studio " }))).toBe("Studio");
    expect(accountName(account("a1", { displayName: "  " }))).toBe("a1@example.com");
  });

  it("turns the events into read-only calendar events in the tenant wall clock and the account colour", () => {
    const [timed] = toCalendarEvents([event()], [account("a1", { color: "teal" })], new Set());
    expect(timed).toMatchObject({
      id: "external-e1",
      title: "Riunione",
      start: "2026-10-20T09:30:00",
      end: "2026-10-20T10:00:00",
      allDay: false,
      editable: false,
      borderColor: "var(--account-teal)",
      classNames: ["external-event"],
    });
    expect(timed?.extendedProps?.external).toMatchObject({ id: "e1" });
  });

  it("keeps the exclusive end of an all-day event and marks a tentative one", () => {
    const [allDay] = toCalendarEvents(
      [event({ allDay: true, time: null, endTime: null, endDate: "2026-10-22", tentative: true })],
      [account("a1")],
      new Set(),
    );
    expect(allDay).toMatchObject({
      start: "2026-10-20",
      end: "2026-10-22",
      allDay: true,
      classNames: ["external-event", "external-event-tentative"],
    });
  });

  it("leaves out the hidden accounts and the events of unknown accounts", () => {
    const events = [
      event(),
      event({ id: "e2", accountId: "a2" }),
      event({ id: "e3", accountId: "x" }),
    ];
    const accounts = [account("a1"), account("a2")];
    expect(toCalendarEvents(events, accounts, new Set(["a1"])).map((item) => item.id)).toEqual([
      "external-e2",
    ]);
    expect(toCalendarEvents(undefined, accounts, new Set())).toEqual([]);
  });

  it("links to the provider only over https", () => {
    expect(safeEventLink("https://calendar.google.com/event?eid=1")).toBe(
      "https://calendar.google.com/event?eid=1",
    );
    expect(safeEventLink("javascript:alert(1)")).toBeUndefined();
    expect(safeEventLink("http://example.com")).toBeUndefined();
    expect(safeEventLink("not a url")).toBeUndefined();
    expect(safeEventLink(null)).toBeUndefined();
  });

  it("tells the active runs", () => {
    expect(isSyncActive({ status: "Queued" })).toBe(true);
    expect(isSyncActive({ status: "Running" })).toBe(true);
    expect(isSyncActive({ status: "Succeeded" })).toBe(false);
    expect(isSyncActive({ status: "Failed" })).toBe(false);
  });
});

describe("hidden accounts", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("are remembered by the browser", () => {
    const store = new Map<string, string>();
    vi.stubGlobal("window", {
      localStorage: {
        getItem: (key: string) => store.get(key) ?? null,
        setItem: (key: string, value: string) => store.set(key, value),
      },
    });

    saveHiddenAccounts(new Set(["a1", "a2"]));
    expect([...loadHiddenAccounts()]).toEqual(["a1", "a2"]);
  });

  it("are none when the storage is blocked or holds something else", () => {
    vi.stubGlobal("window", {
      localStorage: {
        getItem: () => {
          throw new Error("blocked");
        },
        setItem: () => {
          throw new Error("blocked");
        },
      },
    });
    expect(loadHiddenAccounts().size).toBe(0);
    expect(() => saveHiddenAccounts(new Set(["a1"]))).not.toThrow();

    vi.stubGlobal("window", { localStorage: { getItem: () => '{"a":1}' } });
    expect(loadHiddenAccounts().size).toBe(0);
  });
});
