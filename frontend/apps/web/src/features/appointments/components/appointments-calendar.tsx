"use client";

import * as React from "react";
import type { DatesSetArg, EventClickArg, EventDropArg, EventInput } from "@fullcalendar/core";
import enLocale from "@fullcalendar/core/locales/en-gb";
import itLocale from "@fullcalendar/core/locales/it";
import dayGridPlugin from "@fullcalendar/daygrid";
import interactionPlugin, {
  type DateClickArg,
  type EventResizeDoneArg,
} from "@fullcalendar/interaction";
import listPlugin from "@fullcalendar/list";
import FullCalendar from "@fullcalendar/react";
import timeGridPlugin from "@fullcalendar/timegrid";
import { useLocale, useTranslations } from "next-intl";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";

import { useAppointmentCalendar, type AppointmentListItem, type CalendarRange } from "../api";
import { addDays, fromWallClock, nowIn, wallClock } from "../schemas/appointment";

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
 * resize the appointments they manage (`canMove`).
 */
export default function AppointmentsCalendar({
  tenant,
  global,
  clientId,
  staff,
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
  const timeZone = calendar.data?.timeZone;
  const now = nowIn(timeZone);

  const events: EventInput[] = (calendar.data?.items ?? []).map((item) => ({
    id: item.id,
    title: `${staff ? item.client.fullName : item.employee.fullName} · ${t(item.status as "Pending")}`,
    start: wallClock(item.date, item.time),
    end: wallClock(item.date, item.time, Number(item.durationMinutes)),
    classNames: [`appointment-${item.status}`],
    editable: canMove(item),
    extendedProps: { appointment: item },
  }));

  const onDatesSet = (arg: DatesSetArg) => {
    const from = arg.start.toISOString().slice(0, 10);
    const to = addDays(arg.end.toISOString().slice(0, 10), -1);
    setRange((current) => (current?.from === from && current.to === to ? current : { from, to }));
  };

  const onDateClick = (arg: DateClickArg) => {
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

  const moved = (arg: EventDropArg | EventResizeDoneArg) => {
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
      {calendar.error ? (
        <ApiErrorAlert error={calendar.error} onRetry={() => void calendar.refetch()} />
      ) : null}
      <FullCalendar
        plugins={[dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin]}
        locales={[itLocale, enLocale]}
        locale={locale === "it" ? "it" : "en-gb"}
        timeZone="UTC"
        now={`${now.date}T${now.time}:00`}
        initialView={initialView}
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
        slotLabelFormat={{ hour: "2-digit", minute: "2-digit", hour12: false }}
        events={events}
        datesSet={onDatesSet}
        dateClick={onDateClick}
        eventClick={(arg: EventClickArg) => {
          arg.jsEvent.preventDefault();
          onOpen(arg.event.id);
        }}
        eventAllow={(span) => fromWallClock(span.start).date >= now.date}
        eventDrop={moved}
        eventResize={moved}
        noEventsContent={t("app.appointments.none")}
      />
    </div>
  );
}
