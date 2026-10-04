"use client";

import * as React from "react";
import dynamic from "next/dynamic";
import { CalendarIcon, ListIcon, PlusIcon } from "lucide-react";
import { parseAsBoolean, parseAsStringLiteral, useQueryState } from "nuqs";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Switch } from "@auxilia/ui/components/switch";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";
import { useCan, useCurrentUserId, useHasRole } from "@/lib/permissions";

import {
  findConflicts,
  isOpen,
  moveAppointment,
  useAppointmentMutation,
  type AppointmentListItem,
} from "../api";
import { SCHEDULING_PERMISSIONS } from "../permissions";
import { addDays, apiTime, nowIn } from "../schemas/appointment";
import { AppointmentFormDialog } from "./appointment-form-dialog";
import { AppointmentSheet } from "./appointment-sheet";
import type { CalendarMove } from "./appointments-calendar";
import { AppointmentsTable } from "./appointments-table";
import { RequestDialog } from "./request-dialog";

// The calendar is heavy and browser-only: loaded on demand (skill auxilia-frontend-feature).
const AppointmentsCalendar = dynamic(() => import("./appointments-calendar"), {
  ssr: false,
  loading: () => <Skeleton className="h-[32rem] w-full" />,
});

const VIEWS = ["calendar", "list"] as const;

type Creating = { date: string; time?: string } | null;

/**
 * The appointment page (F13, one page for every role): calendar or grid (`?view=`), the staff "global calendar"
 * switch (`?global=`), the drawer of an appointment, creation (staff schedule, clients request) from the button or a
 * day of the calendar, moves by drag & drop with the conflict warning. `clientId` (client 360°) keeps one client and
 * fixes it in the creation.
 */
export function AppointmentsView({
  tenant,
  label,
  clientId,
}: {
  tenant: string;
  label: string;
  clientId?: string;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const me = useCurrentUserId();
  const isAdministrator = useHasRole("Administrator");
  const isEmployee = useHasRole("Employee");
  const isClient = useHasRole("Client");
  const canManage = useCan(SCHEDULING_PERMISSIONS.manage);
  const staff = isAdministrator || isEmployee;
  const [view, setView] = useQueryState(
    "view",
    parseAsStringLiteral(VIEWS).withDefault("calendar").withOptions({ history: "replace" }),
  );
  const [global, setGlobal] = useQueryState(
    "global",
    parseAsBoolean.withDefault(false).withOptions({ history: "replace" }),
  );
  // `?open={id}`: the deep link of a notification (F16) opens the drawer.
  const [opened, setOpened] = useQueryState("open", { history: "replace" });
  const [creating, setCreating] = React.useState<Creating>(null);
  const move = useAppointmentMutation(tenant, (input: CalendarMove) =>
    moveAppointment(input.appointment.id, input.date, apiTime(input.time), input.durationMinutes),
  );

  const canCreate = canManage && (staff || isClient);
  const showGlobal = staff && clientId === undefined;
  const tomorrow = addDays(nowIn(undefined).date, 1);

  // Administrators move every open appointment, employees their own; clients ask for a new one instead (Q21).
  const canMove = (appointment: AppointmentListItem) =>
    canManage &&
    isOpen(appointment.status) &&
    (isAdministrator || (isEmployee && appointment.employee.userId === me));

  const onMove = async (input: CalendarMove) => {
    try {
      const conflicts = await findConflicts({
        employeeUserId: input.appointment.employee.userId,
        date: input.date,
        time: apiTime(input.time),
        durationMinutes: input.durationMinutes,
        excludeId: input.appointment.id,
      });
      const proceed = await confirm({
        title: conflicts.length > 0 ? t("app.appointments.conflictTitle") : undefined,
        description:
          conflicts.length > 0
            ? t("app.appointments.conflictDescription", {
                list: conflicts.map((conflict) => conflict.clientName).join(", "),
              })
            : t("app.appointments.moveConfirm", {
                when: `${format.dateTime(new Date(`${input.date}T00:00:00`), { dateStyle: "medium" })} ${input.time}`,
              }),
        confirmLabel: conflicts.length > 0 ? t("app.appointments.saveAnyway") : t("Save"),
      });
      if (!proceed) {
        input.revert();
        return;
      }

      await move.mutateAsync(input);
      notify.success("app.appointments.moved");
    } catch (error) {
      input.revert();
      notify.error(error);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <div
            className="inline-flex rounded-md border p-0.5"
            role="group"
            aria-label={t("app.appointments.views")}
          >
            <Button
              type="button"
              size="sm"
              variant={view === "calendar" ? "secondary" : "ghost"}
              aria-pressed={view === "calendar"}
              onClick={() => void setView("calendar")}
            >
              <CalendarIcon aria-hidden /> {t("app.appointments.calendar")}
            </Button>
            <Button
              type="button"
              size="sm"
              variant={view === "list" ? "secondary" : "ghost"}
              aria-pressed={view === "list"}
              onClick={() => void setView("list")}
            >
              <ListIcon aria-hidden /> {t("AppointmentsList")}
            </Button>
          </div>
          {showGlobal && view === "calendar" ? (
            <span className="flex items-center gap-2">
              <Switch
                id="appointments-global"
                checked={global}
                onCheckedChange={(value) => void setGlobal(value)}
              />
              <Label htmlFor="appointments-global">{t("app.appointments.globalCalendar")}</Label>
            </span>
          ) : null}
        </div>
        {canCreate ? (
          <Button type="button" onClick={() => setCreating({ date: tomorrow })}>
            <PlusIcon aria-hidden /> {staff ? t("AppointmentNew") : t("RequestAppointment")}
          </Button>
        ) : null}
      </div>

      {view === "calendar" ? (
        <AppointmentsCalendar
          tenant={tenant}
          global={showGlobal && global}
          clientId={clientId}
          staff={staff}
          canCreate={canCreate}
          canMove={canMove}
          onCreate={(date, time) => setCreating({ date, time })}
          onOpen={(id) => void setOpened(id)}
          onMove={(input) => void onMove(input)}
        />
      ) : (
        <AppointmentsTable
          tenant={tenant}
          label={label}
          clientId={clientId}
          onOpen={(id) => void setOpened(id)}
        />
      )}

      <AppointmentSheet tenant={tenant} id={opened} onClose={() => void setOpened(null)} />
      {creating && staff ? (
        <AppointmentFormDialog
          tenant={tenant}
          open
          onOpenChange={(open) => (open ? undefined : setCreating(null))}
          clientId={clientId}
          initial={{ date: creating.date, time: creating.time ?? "09:00" }}
          onSaved={(id) => void setOpened(id)}
        />
      ) : null}
      {creating && !staff ? (
        <RequestDialog
          tenant={tenant}
          open
          onOpenChange={(open) => (open ? undefined : setCreating(null))}
          date={creating.date}
          time={creating.time}
          onSaved={(id) => void setOpened(id)}
        />
      ) : null}
    </div>
  );
}
