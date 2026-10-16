"use client";

import * as React from "react";
import Link from "next/link";
import type { EventInput } from "@fullcalendar/core";
import { CircleAlertIcon, LinkIcon, LoaderCircleIcon, RefreshCwIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";
import { useRealtimeConnected } from "@/lib/realtime/realtime";

import {
  CONNECT_ACCOUNTS_PERMISSION,
  VIEW_MAIL_PERMISSION,
  isSyncActive,
  useConnectedAccounts,
  useExternalCalendarEvents,
  useStartSync,
  useSyncStatus,
  type AccountSync,
  type ConnectedAccount,
} from "../api";
import {
  accountName,
  calendarAccounts,
  loadHiddenAccounts,
  saveHiddenAccounts,
  toCalendarEvents,
} from "../calendar";
import { accountColorStyle } from "../colors";

export type ExternalCalendar = {
  /** The signed-in user may see their connected calendars (module `mailbox` visible to the role). */
  enabled: boolean;
  /** The accounts have been read (until then the bar shows nothing, not the "connect" hint). */
  ready: boolean;
  /** The accounts with the calendar capability, oldest first. */
  accounts: ConnectedAccount[];
  /** Events of the shown accounts for the visible range, ready for FullCalendar. */
  events: EventInput[];
  hidden: ReadonlySet<string>;
  toggle: (accountId: string) => void;
  error: unknown;
  retry: () => void;
};

/**
 * The connected calendars inside the appointment calendar (N05 #101): the accounts with the calendar capability,
 * their copied events for the visible range (local days of the tenant) and which accounts the legend hides
 * (remembered by the browser).
 */
export function useExternalCalendar(
  tenant: string,
  range: { from: string; to: string } | null,
  wanted: boolean,
): ExternalCalendar {
  const canConnect = useCan(CONNECT_ACCOUNTS_PERMISSION);
  const canView = useCan(VIEW_MAIL_PERMISSION);
  const enabled = wanted && canConnect && canView;
  const accounts = useConnectedAccounts(tenant, enabled);
  const withCalendar = React.useMemo(() => calendarAccounts(accounts.data), [accounts.data]);
  const events = useExternalCalendarEvents(tenant, range, enabled && withCalendar.length > 0);
  // Browser-only component (the calendar is loaded without SSR): the stored choice is read at once.
  const [hidden, setHidden] = React.useState<ReadonlySet<string>>(loadHiddenAccounts);

  const toggle = React.useCallback((accountId: string) => {
    setHidden((current) => {
      const next = new Set(current);
      if (!next.delete(accountId)) {
        next.add(accountId);
      }

      saveHiddenAccounts(next);
      return next;
    });
  }, []);

  return {
    enabled,
    ready: accounts.isSuccess,
    accounts: withCalendar,
    events: React.useMemo(
      () => (enabled ? toCalendarEvents(events.data?.items, withCalendar, hidden) : []),
      [enabled, events.data, withCalendar, hidden],
    ),
    hidden,
    toggle,
    error: accounts.error ?? events.error,
    retry: () => void (accounts.error ? accounts.refetch() : events.refetch()),
  };
}

/**
 * Above the calendar (N05 #101/#106): opening the page asks for a calendar sync once (the stored events show at
 * once), the "Synchronising…" bar stays while a run of an account is queued or running (the realtime
 * `AccountSyncCompleted` refreshes the queries; without the hub the status is polled), "Sync now", the legend that
 * shows or hides each account in its colour, and the accounts to reconnect.
 */
export function ExternalCalendarBar({
  tenant,
  calendar,
}: {
  tenant: string;
  calendar: ExternalCalendar;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const connected = useRealtimeConnected();
  const { enabled, accounts } = calendar;
  const syncable = accounts.some((account) => account.status === "Active");
  const status = useSyncStatus(tenant, "calendar", enabled && syncable, !connected);
  const start = useStartSync(tenant, "calendar");
  const runs = status.data ?? [];
  const active = runs.some(isSyncActive);
  const succeeded = runs.some((run) => run.status === "Succeeded");
  const opened = React.useRef(false);
  const wasActive = React.useRef(false);

  // The page opening starts a sync once (a recent or running one is returned by the API instead).
  React.useEffect(() => {
    if (!enabled || !syncable || opened.current) {
      return;
    }

    opened.current = true;
    start.mutate(false);
  }, [enabled, syncable, start]);

  // A short toast when the runs seen running on this page are over.
  React.useEffect(() => {
    if (wasActive.current && !active && succeeded) {
      notify.success("app.externalCalendar.syncCompleted");
    }

    wasActive.current = active;
  }, [active, succeeded, notify]);

  if (!enabled || !calendar.ready) {
    return null;
  }

  if (accounts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        <Link
          className="inline-flex items-center gap-1 underline-offset-4 hover:underline"
          href={tenantHref(tenant, "/profile?tab=accounts")}
        >
          <LinkIcon aria-hidden className="size-4" />
          {t("app.externalCalendar.connect")}
        </Link>
      </p>
    );
  }

  const failed = runs.filter((run) => run.status === "Failed" && run.errorCode);
  const reconnect = accounts.filter((account) => account.status !== "Active");

  return (
    <section className="flex flex-col gap-2" aria-label={t("app.externalCalendar.legend")}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <ul className="flex flex-wrap items-center gap-2">
          {accounts.map((account) => {
            const shown = !calendar.hidden.has(account.id);
            return (
              <li key={account.id}>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  aria-pressed={shown}
                  title={t("app.externalCalendar.toggle")}
                  className={shown ? undefined : "text-muted-foreground line-through"}
                  onClick={() => calendar.toggle(account.id)}
                >
                  <span
                    aria-hidden
                    className="size-3 shrink-0 rounded-full"
                    style={shown ? accountColorStyle(account.color) : undefined}
                  />
                  {accountName(account)}
                  {account.status !== "Active" ? (
                    <CircleAlertIcon aria-hidden className="text-destructive" />
                  ) : null}
                </Button>
              </li>
            );
          })}
        </ul>
        {syncable ? (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={active || start.isPending}
            onClick={() => start.mutate(true, { onError: (error) => notify.error(error) })}
          >
            <RefreshCwIcon aria-hidden /> {t("app.externalCalendar.syncNow")}
          </Button>
        ) : null}
      </div>
      <div role="status" aria-live="polite" className="text-sm">
        {active ? (
          <p className="flex items-center gap-2 rounded-md border bg-muted px-3 py-2 text-muted-foreground">
            <LoaderCircleIcon
              aria-hidden
              className="size-4 animate-spin motion-reduce:animate-none"
            />
            {t("app.externalCalendar.syncing")}
          </p>
        ) : null}
      </div>
      {failed.length > 0 || reconnect.length > 0 ? (
        <ul className="flex flex-col gap-1 text-sm text-destructive">
          {reconnect.map((account) => (
            <li key={`reconnect-${account.id}`} className="flex flex-wrap items-center gap-1">
              <CircleAlertIcon aria-hidden className="size-4" />
              {t("app.externalCalendar.reconnect", { name: accountName(account) })}
              <Link
                className="underline underline-offset-4"
                href={tenantHref(tenant, "/profile?tab=accounts")}
              >
                {t("app.connectedAccounts.reconnect")}
              </Link>
            </li>
          ))}
          {failed
            .filter((run) => !reconnect.some((account) => account.id === run.accountId))
            .map((run) => (
              <FailedRun key={run.runId} run={run} accounts={accounts} />
            ))}
        </ul>
      ) : null}
    </section>
  );
}

function FailedRun({ run, accounts }: { run: AccountSync; accounts: ConnectedAccount[] }) {
  const t = useTranslations();
  const account = accounts.find((item) => item.id === run.accountId);
  if (!account) {
    return null;
  }

  const key = `errors.${run.errorCode}`;
  return (
    <li className="flex items-center gap-1">
      <CircleAlertIcon aria-hidden className="size-4" />
      {t("app.externalCalendar.failed", {
        name: accountName(account),
        message: t.has(key) ? t(key) : t("errors.generic"),
      })}
    </li>
  );
}
