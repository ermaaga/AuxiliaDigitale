"use client";

import * as React from "react";
import FullCalendar, {
  type DateClickInfo,
  type DatesSetInfo,
  type EventClickInfo,
  type EventDropInfo,
  type EventInput,
  type EventResizeDoneInfo,
} from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/react/daygrid";
import interactionPlugin from "@fullcalendar/react/interaction";
import listPlugin from "@fullcalendar/react/list";
import enLocale from "@fullcalendar/react/locales/en-gb";
import itLocale from "@fullcalendar/react/locales/it";
import timeGridPlugin from "@fullcalendar/react/timegrid";
import classicThemePlugin from "@fullcalendar/react/themes/classic";
import "@fullcalendar/react/skeleton.css";
import "@fullcalendar/react/themes/classic/theme.css";
import { useLocale, useTranslations } from "next-intl";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import {
  ExternalCalendarBar,
  ExternalEventDialog,
  useExternalCalendar,
  type ExternalCalendarEvent,
} from "@/features/mailbox";

import { useAppointmentCalendar, type AppointmentListItem, type CalendarRange } from "../api";
import { addDays, fromWallClock, nowIn, wallClock } from "../schemas/appointment";

/**
 * Colours of an appointment by status (FullCalendar 7 sets them as `--fc-event-color`/`--fc-event-contrast-color` on
 * the event); the other statuses keep the calendar's primary colour. The palette of the theme is in `globals.css`.
 */
const STATUS_COLORS: Partial<Record<string, { color: string; contrastColor: string }>> = {
  Pending: { color: "var(--warning)", contrastColor: "var(--warning-foreground)" },
  Rejected: { color: "var(--muted)", contrastColor: "var(--muted-foreground)" },
  Cancelled: { color: "var(--muted)", contrastColor: "var(--muted-foreground)" },
  Completed: { color: "var(--secondary)", contrastColor: "var(--secondary-foreground)" },
};

export type CalendarMove = {
  appointment: AppointmentListItem;
  date: string;
  time: string;
  durationMinutes: number;
  revert: () => void;
};

/**
 * The appointment calendar (F13, skill auxilia-ui-design): month, week, day and list views (list on phones), the
 * tenant's wall-clock times (FullCalendar in UTC with the local times of the API, so the browser time zone never
 * shifts them). A click on a day or slot from today on creates, a click on an appointment opens it; staff move and
 * resize the appointments they manage (`canMove`). `external` adds the user's connected calendars (N05 #101): one
 * read-only source per account in its colour, with the legend and the sync bar above.
 */
export default function AppointmentsCalendar({
  tenant,
  global,
  clientId,
  staff,
  external,
  canCreate,
  canMove,
  onCreate,
  onOpen,
  onMove,
}: {
  tenant: string;
  global: boolean;
  clientId?: string;
  staff: boolean;
  external: boolean;
  canCreate: boolean;
  canMove: (appointment: AppointmentListItem) => boolean;
  onCreate: (date: string, time?: string) => void;
  onOpen: (id: string) => void;
  onMove: (move: CalendarMove) => void;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const [range, setRange] = React.useState<{ from: string; to: string } | null>(null);
  const [initialView] = React.useState(() =>
    typeof window !== "undefined" && window.matchMedia("(max-width: 639px)").matches
      ? "listWeek"
      : "dayGridMonth",
  );
  const query: CalendarRange | null = range ? { ...range, global, clientId } : null;
  const calendar = useAppointmentCalendar(tenant, query);
  const connected = useExternalCalendar(tenant, range, external);
  const [shownEvent, setShownEvent] = React.useState<ExternalCalendarEvent | null>(null);
  const timeZone = calendar.data?.timeZone;
  const now = nowIn(timeZone);

  const appointments: EventInput[] = (calendar.data?.items ?? []).map((item) => ({
    id: item.id,
    title: `${staff ? item.client.fullName : item.employee.fullName} · ${t(item.status as "Pending")}`,
    start: wallClock(item.date, item.time),
    end: wallClock(item.date, item.time, Number(item.durationMinutes)),
    className: `appointment-${item.status}`,
    ...STATUS_COLORS[item.status],
    editable: canMove(item),
    extendedProps: { appointment: item },
  }));
  const events =
    connected.events.length > 0 ? [...appointments, ...connected.events] : appointments;

  const onDatesSet = (arg: DatesSetInfo) => {
    const from = arg.start.toISOString().slice(0, 10);
    const to = addDays(arg.end.toISOString().slice(0, 10), -1);
    setRange((current) => (current?.from === from && current.to === to ? current : { from, to }));
  };

  const onDateClick = (arg: DateClickInfo) => {
    const { date, time } = fromWallClock(arg.date);
    if (
      !canCreate ||
      date < now.date ||
      (!arg.allDay && `${date}T${time}` <= `${now.date}T${now.time}`)
    ) {
      return;
    }

    onCreate(date, arg.allDay ? undefined : time);
  };

  const moved = (arg: EventDropInfo | EventResizeDoneInfo) => {
    const appointment = arg.event.extendedProps.appointment as AppointmentListItem;
    const start = arg.event.start!;
    const end =
      arg.event.end ?? new Date(start.getTime() + Number(appointment.durationMinutes) * 60_000);
    const { date, time } = fromWallClock(start);
    onMove({
      appointment,
      date,
      time,
      durationMinutes: Math.round((end.getTime() - start.getTime()) / 60_000),
      revert: arg.revert,
    });
  };

  return (
    <div className="flex flex-col gap-2" aria-busy={calendar.isFetching || undefined}>
      <ExternalCalendarBar tenant={tenant} calendar={connected} />
      {calendar.error ? (
        <ApiErrorAlert error={calendar.error} onRetry={() => void calendar.refetch()} />
      ) : null}
      {connected.error ? <ApiErrorAlert error={connected.error} onRetry={connected.retry} /> : null}
      <FullCalendar
        plugins={[classicThemePlugin, dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin]}
        className="aux-calendar"
        locales={[itLocale, enLocale]}
        locale={locale === "it" ? "it" : "en-gb"}
        timeZone="UTC"
        now={`${now.date}T${now.time}:00`}
        initialView={initialView}
        // Text buttons (the locale's "previous"/"next"), as with FullCalendar 6. No hints: FullCalendar 7 writes them
        // as `aria-label` (in English for every language) instead of the visible text, so speech users could not say
        // what they see (WCAG 2.5.3); empty, the name of a button is its text again.
        buttonDisplay="text"
        prevHint=""
        nextHint=""
        todayHint=""
        viewHint=""
        moreLinkHint=""
        headerToolbar={{
          left: "prev,next today",
          center: "title",
          right: "dayGridMonth,timeGridWeek,timeGridDay,listWeek",
        }}
        height="auto"
        dayMaxEvents={2}
        nowIndicator
        slotMinTime="07:00:00"
        slotMaxTime="21:00:00"
        scrollTime="08:00:00"
        eventTimeFormat={{ hour: "2-digit", minute: "2-digit", hour12: false }}
        slotHeaderFormat={{ hour: "2-digit", minute: "2-digit", hour12: false }}
        events={events}
        datesSet={onDatesSet}
        dateClick={onDateClick}
        eventClick={(arg: EventClickInfo) => {
          arg.jsEvent.preventDefault();
          const externalEvent = arg.event.extendedProps.external as
            ExternalCalendarEvent | undefined;
          if (externalEvent) {
            setShownEvent(externalEvent);
          } else {
            onOpen(arg.event.id);
          }
        }}
        eventAllow={(span) => fromWallClock(span.start).date >= now.date}
        eventDrop={moved}
        eventResize={moved}
        noEventsContent={t("app.appointments.none")}
      />
      <ExternalEventDialog
        event={shownEvent}
        accounts={connected.accounts}
        onClose={() => setShownEvent(null)}
      />
    </div>
  );
}
