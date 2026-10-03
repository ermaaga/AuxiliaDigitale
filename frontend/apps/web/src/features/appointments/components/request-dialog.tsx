"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Textarea } from "@auxilia/ui/components/textarea";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { requestAppointment, useAppointmentEmployees, useAppointmentMutation } from "../api";
import {
  apiTime,
  DEFAULT_DURATION,
  NOTES_MAX,
  requestSchema,
  type RequestOutput,
} from "../schemas/appointment";

const FIELDS = ["employeeUserId", "date", "time", "durationMinutes", "notes"] as const;
const ALIASES = { startsAt: "date" } as const;

/**
 * A client asks for an appointment (F13, legacy client page, Q22): any active operator (the one in charge
 * preselected), preferred day (default tomorrow or the day clicked) at 09:00, duration 60, notes. It stays pending
 * until the operator approves or rejects it.
 */
export function RequestDialog({
  tenant,
  open,
  onOpenChange,
  date,
  time,
  onSaved,
}: {
  tenant: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  date: string;
  time?: string;
  onSaved?: (id: string) => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const employees = useAppointmentEmployees(tenant, open);
  const request = useAppointmentMutation(tenant, (values: RequestOutput) =>
    requestAppointment({
      employeeUserId: values.employeeUserId,
      date: values.date,
      time: apiTime(values.time),
      durationMinutes: Number(values.durationMinutes),
      notes: values.notes.trim() || null,
    }),
  );
  const form = useZodForm(requestSchema, {
    defaultValues: {
      employeeUserId: "",
      date,
      time: time ?? "09:00",
      durationMinutes: String(DEFAULT_DURATION),
      notes: "",
    },
  });

  // The operator in charge is preselected once the list arrives.
  const inCharge = employees.data?.find((employee) => employee.inCharge)?.userId;
  React.useEffect(() => {
    if (inCharge && !form.getValues("employeeUserId")) {
      form.setValue("employeeUserId", inCharge);
    }
  }, [inCharge, form]);

  const submit = form.handleSubmit(async (values) => {
    try {
      const created = await request.mutateAsync(values);
      notify.success("app.appointments.requested");
      onOpenChange(false);
      onSaved?.(created.id);
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
          <DialogTitle>{t("RequestAppointment")}</DialogTitle>
          <DialogDescription>{t("app.appointments.requestDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {request.error &&
          !(isApiError(request.error) && Object.keys(request.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={request.error} />
          ) : null}
          <FormField
            control={form.control}
            name="employeeUserId"
            label={`${t("app.appointments.operator")} *`}
          >
            {(field, props) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger {...props}>
                  <SelectValue placeholder={t("app.appointments.chooseOperator")} />
                </SelectTrigger>
                <SelectContent>
                  {(employees.data ?? []).map((employee) => (
                    <SelectItem key={employee.userId} value={employee.userId}>
                      {employee.inCharge
                        ? `${employee.fullName} (${t("app.appointments.yourOperator")})`
                        : employee.fullName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </FormField>
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
          <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
            {t("RequestAppointment")}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
