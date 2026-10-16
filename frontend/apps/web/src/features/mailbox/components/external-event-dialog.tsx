"use client";

import { ExternalLinkIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";

import type { ConnectedAccount, ExternalCalendarEvent } from "../api";
import { accountName, safeEventLink } from "../calendar";
import { accountColorStyle } from "../colors";
import { providerLabel } from "./provider";

/**
 * The details of an event of a connected calendar (N05 #101): read only — title, when (tenant wall clock), all-day,
 * location, the account in its colour and a link to open it at the provider. It never becomes an appointment.
 */
export function ExternalEventDialog({
  event,
  accounts,
  onClose,
}: {
  event: ExternalCalendarEvent | null;
  accounts: readonly ConnectedAccount[];
  onClose: () => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const account = event ? accounts.find((item) => item.id === event.accountId) : undefined;
  const link = safeEventLink(event?.link);

  // Wall-clock dates and times of the tenant, formatted without moving them to the browser time zone.
  const day = (date: string) =>
    format.dateTime(new Date(`${date}T00:00:00Z`), { timeZone: "UTC", dateStyle: "full" });
  const clock = (time: string) => time.slice(0, 5);
  const when = (item: ExternalCalendarEvent) => {
    if (item.allDay) {
      const last = new Date(`${item.endDate}T00:00:00Z`);
      last.setUTCDate(last.getUTCDate() - 1);
      const lastDay = last.toISOString().slice(0, 10);
      return lastDay > item.date
        ? `${day(item.date)} – ${day(lastDay)}`
        : `${day(item.date)} · ${t("app.externalCalendar.allDay")}`;
    }

    const start = `${day(item.date)} ${clock(item.time ?? "00:00")}`;
    const end = item.endTime ? clock(item.endTime) : "";
    return item.endDate === item.date
      ? `${start} – ${end}`
      : `${start} – ${day(item.endDate)} ${end}`;
  };

  return (
    <Dialog open={event !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")} className="sm:max-w-md">
        {event ? (
          <>
            <DialogHeader>
              <DialogTitle>{event.title || t("app.externalCalendar.untitled")}</DialogTitle>
              <DialogDescription>{t("app.externalCalendar.readOnly")}</DialogDescription>
            </DialogHeader>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
              <dt className="text-muted-foreground">{t("app.externalCalendar.when")}</dt>
              <dd>
                {when(event)}
                {event.tentative ? (
                  <Badge variant="outline" className="ms-2">
                    {t("app.externalCalendar.tentative")}
                  </Badge>
                ) : null}
              </dd>
              {event.location ? (
                <>
                  <dt className="text-muted-foreground">{t("app.externalCalendar.location")}</dt>
                  <dd className="break-words">{event.location}</dd>
                </>
              ) : null}
              {account ? (
                <>
                  <dt className="text-muted-foreground">{t("app.connectedAccounts.calendar")}</dt>
                  <dd className="flex items-center gap-2">
                    <span
                      aria-hidden
                      className="size-3 shrink-0 rounded-full"
                      style={accountColorStyle(account.color)}
                    />
                    <span className="break-all">{accountName(account)}</span>
                  </dd>
                </>
              ) : null}
            </dl>
            <DialogFooter>
              {link && account ? (
                <Button asChild variant="outline">
                  <a href={link} target="_blank" rel="noopener noreferrer">
                    <ExternalLinkIcon aria-hidden />
                    {t("app.externalCalendar.openAt", {
                      provider: providerLabel(t, account.provider),
                    })}
                  </a>
                </Button>
              ) : null}
              <Button type="button" onClick={onClose}>
                {t("Close")}
              </Button>
            </DialogFooter>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
