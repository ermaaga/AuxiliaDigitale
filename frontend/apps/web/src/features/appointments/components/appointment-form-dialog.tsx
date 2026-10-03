"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Switch } from "@auxilia/ui/components/switch";
import { Textarea } from "@auxilia/ui/components/textarea";

import { Combobox } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useAssignableEmployees, useClients } from "@/features/clients";
import { useNotify } from "@/lib/notify";
import { useCurrentUserId, useHasRole } from "@/lib/permissions";

import {
  findConflicts,
  scheduleAppointment,
  updateAppointment,
  useAppointmentMutation,
  type AppointmentConflict,
  type AppointmentDetail,
} from "../api";
import {
  apiTime,
  appointmentSchema,
  DEFAULT_DURATION,
  NOTES_MAX,
  shortTime,
  type AppointmentOutput,
} from "../schemas/appointment";

const FIELDS = ["clientId", "employeeUserId", "date", "time", "durationMinutes", "notes"] as const;
const ALIASES = { startsAt: "date" } as const;

/**
 * Staff schedule or edit an appointment (F13, legacy `AppointmentModal`): client (employees: the clients in charge;
 * fixed in the client 360° and when editing), employee (Administrators only, default: the one in charge), date and
 * time in the tenant time zone, duration, notes, global calendar. Overlapping appointments of the employee are a
 * warning to confirm, never a block.
 */
export function AppointmentFormDialog({
  tenant,
  open,
  onOpenChange,
  appointment,
  clientId,
  initial,
  onSaved,
}: {
  tenant: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  appointment?: AppointmentDetail;
  clientId?: string;
  initial?: { date: string; time: string };
  onSaved?: (id: string) => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const format = useFormatter();
  const me = useCurrentUserId();
  const isAdministrator = useHasRole("Administrator");
  const [clientSearch, setClientSearch] = React.useState("");
  const editing = appointment !== undefined;
  const chooseClient = !editing && clientId === undefined;
  const clients = useClients(tenant, {
    view: isAdministrator ? "all" : "mine",
    page: 1,
    pageSize: 20,
    "filter[fullName]": clientSearch || undefined,
  });
  const employees = useAssignableEmployees(tenant, open && isAdministrator && !editing);
  const save = useAppointmentMutation(tenant, (values: AppointmentOutput) => {
    const slot = {
      date: values.date,
      time: apiTime(values.time),
      durationMinutes: Number(values.durationMinutes),
      notes: values.notes.trim() || null,
      showInGlobalCalendar: values.showInGlobalCalendar,
      customFields: null,
    };
    return editing
      ? updateAppointment(appointment.id, { ...slot, customFields: appointment.customFields })
      : scheduleAppointment({
          ...slot,
          clientId: values.clientId,
          employeeUserId: isAdministrator && values.employeeUserId ? values.employeeUserId : null,
        });
  });
  const form = useZodForm(appointmentSchema, {
    defaultValues: {
      clientId: appointment?.client.id ?? clientId ?? "",
      employeeUserId: "",
      date: appointment?.date ?? initial?.date ?? "",
      time: appointment ? shortTime(appointment.time) : (initial?.time ?? "09:00"),
      durationMinutes: String(appointment?.durationMinutes ?? DEFAULT_DURATION),
      notes: appointment?.notes ?? "",
      showInGlobalCalendar: appointment?.showInGlobalCalendar ?? true,
    },
  });

  /** The employee whose time the slot takes, when the page knows it. */
  const employeeOf = (values: AppointmentOutput) =>
    appointment?.employee.userId ?? (isAdministrator ? values.employeeUserId || undefined : me);

  const confirmConflicts = async (conflicts: readonly AppointmentConflict[]) =>
    conflicts.length === 0 ||
    (await confirm({
      title: t("app.appointments.conflictTitle"),
      description: t("app.appointments.conflictDescription", {
        list: conflicts
          .map(
            (conflict) =>
              `${conflict.clientName} (${format.dateTime(new Date(conflict.startsAt), { timeStyle: "short" })}–${format.dateTime(new Date(conflict.endsAt), { timeStyle: "short" })})`,
          )
          .join(", "),
      }),
      confirmLabel: t("app.appointments.saveAnyway"),
    }));

  const submit = form.handleSubmit(async (values) => {
    try {
      const employee = employeeOf(values);
      if (employee) {
        const conflicts = await findConflicts({
          employeeUserId: employee,
          date: values.date,
          time: apiTime(values.time),
          durationMinutes: Number(values.durationMinutes),
          excludeId: appointment?.id,
        });
        if (!(await confirmConflicts(conflicts))) {
          return;
        }
      }

      const saved = await save.mutateAsync(values);
      notify.success(editing ? "app.appointments.saved" : "app.appointments.scheduled");
      onOpenChange(false);
      onSaved?.(saved.id);
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, FIELDS, ALIASES);
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{editing ? t("app.appointments.edit") : t("AppointmentNew")}</DialogTitle>
          <DialogDescription>{t("app.appointments.formDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {save.error &&
          !(isApiError(save.error) && Object.keys(save.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          {chooseClient ? (
            <FormField control={form.control} name="clientId" label={`${t("Client")} *`}>
              {(field, props) => (
                <Combobox
                  {...props}
                  options={(clients.data?.items ?? []).map((client) => ({
                    value: client.id,
                    label: `${client.lastName} ${client.firstName}`,
                    description: client.email ?? undefined,
                  }))}
                  value={field.value || undefined}
                  onChange={(value) => field.onChange(value ?? "")}
                  placeholder={t("app.documents.chooseClient")}
                  onSearch={setClientSearch}
                  loading={clients.isFetching}
                />
              )}
            </FormField>
          ) : null}
          {isAdministrator && !editing ? (
            <FormField
              control={form.control}
              name="employeeUserId"
              label={t("Employee")}
              description={t("app.appointments.employeeHint")}
            >
              {(field, props) => (
                <Combobox
                  {...props}
                  options={(employees.data ?? []).map((employee) => ({
                    value: employee.userId,
                    label: employee.fullName,
                  }))}
                  value={field.value || undefined}
                  onChange={(value) => field.onChange(value ?? "")}
                  placeholder={t("app.appointments.employeeInCharge")}
                  loading={employees.isFetching}
                />
              )}
            </FormField>
          ) : null}
          <div className="grid gap-4 sm:grid-cols-3">
            <FormField control={form.control} name="date" label={`${t("Date")} *`}>
              {(field, props) => <Input {...field} {...props} type="date" />}
            </FormField>
            <FormField control={form.control} name="time" label={`${t("Time")} *`}>
              {(field, props) => <Input {...field} {...props} type="time" step={300} />}
            </FormField>
            <FormField
              control={form.control}
              name="durationMinutes"
              label={`${t("DurationMinutes")} *`}
            >
              {(field, props) => (
                <Input {...field} {...props} inputMode="numeric" autoComplete="off" />
              )}
            </FormField>
          </div>
          <FormField control={form.control} name="notes" label={t("Notes")}>
            {(field, props) => <Textarea {...field} {...props} rows={3} maxLength={NOTES_MAX} />}
          </FormField>
          <FormField
            control={form.control}
            name="showInGlobalCalendar"
            label={t("ShowInGlobalCalendar")}
          >
            {(field, props) => (
              <span className="flex items-center gap-2">
                <Switch {...props} checked={field.value} onCheckedChange={field.onChange} />
                <Label htmlFor={props.id} className="font-normal">
                  {field.value ? t("Yes") : t("No")}
                </Label>
              </span>
            )}
          </FormField>
          <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
            {editing ? t("Save") : t("Create")}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
